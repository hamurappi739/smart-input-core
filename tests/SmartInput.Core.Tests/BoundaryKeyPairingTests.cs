using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Input;
using SmartInput.Platform.Windows.Services;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.Core.Tests;

public class BoundaryKeyPairingTrackerTests
{
    [Fact]
    public void RegisterKeyDown_ThenMatchingKeyUp_SuppressesOnceAndReturnsIdle()
    {
        var tracker = new BoundaryKeyPairingTracker();

        tracker.RegisterSuppressedKeyDown(VirtualKeys.Space, 57);
        Assert.Equal(BoundaryPairingState.AwaitingPhysicalKeyUp, tracker.State);

        Assert.True(tracker.TrySuppressMatchingKeyUp(VirtualKeys.Space, 57));
        Assert.Equal(BoundaryPairingState.Idle, tracker.State);
        Assert.Equal(1, tracker.KeyUpSuppressedCount);
    }

    [Fact]
    public void UnrelatedKeyUp_IsNotSuppressed()
    {
        var tracker = new BoundaryKeyPairingTracker();

        tracker.RegisterSuppressedKeyDown(VirtualKeys.Space, 57);

        Assert.False(tracker.TrySuppressMatchingKeyUp(VirtualKeys.Left, 75));
        Assert.Equal(BoundaryPairingState.AwaitingPhysicalKeyUp, tracker.State);
        Assert.Equal(0, tracker.KeyUpSuppressedCount);
    }

    [Fact]
    public void TabAndEnter_PairByVirtualKey()
    {
        var tracker = new BoundaryKeyPairingTracker();

        tracker.RegisterSuppressedKeyDown(VirtualKeys.Tab, 15);
        Assert.True(tracker.TrySuppressMatchingKeyUp(VirtualKeys.Tab, 15));

        tracker.RegisterSuppressedKeyDown(VirtualKeys.Return, 28);
        Assert.True(tracker.TrySuppressMatchingKeyUp(VirtualKeys.Return, 28));

        Assert.Equal(2, tracker.KeyUpSuppressedCount);
    }

    [Fact]
    public void PunctuationBoundary_PairsByVirtualKey()
    {
        var tracker = new BoundaryKeyPairingTracker();

        tracker.RegisterSuppressedKeyDown(VirtualKeys.OemComma, 51);
        Assert.True(tracker.TrySuppressMatchingKeyUp(VirtualKeys.OemComma, 51));
    }

    [Fact]
    public void ClearPending_OnFocusChange_PreventsLaterKeyUpMatch()
    {
        var tracker = new BoundaryKeyPairingTracker();

        tracker.RegisterSuppressedKeyDown(VirtualKeys.Space, 57);
        tracker.ClearPending(BoundaryPairingCleanupReason.FocusChange);

        Assert.False(tracker.TrySuppressMatchingKeyUp(VirtualKeys.Space, 57));
        Assert.Equal(BoundaryPairingState.Idle, tracker.State);
    }

    [Fact]
    public void ClearPending_OnShutdown_PreventsLaterKeyUpMatch()
    {
        var tracker = new BoundaryKeyPairingTracker();

        tracker.RegisterSuppressedKeyDown(VirtualKeys.Space, 57);
        tracker.ClearPending(BoundaryPairingCleanupReason.HookShutdown);

        Assert.False(tracker.TrySuppressMatchingKeyUp(VirtualKeys.Space, 57));
    }

    [Fact]
    public void ExpiredPendingKeyUp_DoesNotSuppressLateRelease()
    {
        var tracker = new BoundaryKeyPairingTracker(keyUpWaitMilliseconds: 1);

        tracker.RegisterSuppressedKeyDown(VirtualKeys.Space, 57);
        Thread.Sleep(20);

        Assert.False(tracker.TrySuppressMatchingKeyUp(VirtualKeys.Space, 57));
        Assert.Equal(BoundaryPairingState.Idle, tracker.State);
    }
}

public class BoundaryKeyPairingInterceptorTests
{
    [Fact]
    public void SpaceKeyDown_SuppressesAndRegistersPairing()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false);

        var keyDown = CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyDown, scanCode: 57);
        var result = interceptor.TryIntercept(keyDown, GenuineMetadata());

        Assert.True(result.ShouldSuppress);
        Assert.False(result.IsPhysicalKeyUpSuppression);
        Assert.NotNull(result.Boundary);
        Assert.Equal(BoundaryPairingState.AwaitingPhysicalKeyUp, tracker.State);
    }

    [Fact]
    public void MatchingSpaceKeyUp_IsSuppressedWithoutBoundaryPayload()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false);

        interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyDown, 57),
            GenuineMetadata());

        var keyUp = CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyUp, 57);
        var result = interceptor.TryIntercept(keyUp, GenuineMetadata());

        Assert.True(result.ShouldSuppress);
        Assert.True(result.IsPhysicalKeyUpSuppression);
        Assert.Null(result.Boundary);
        Assert.Equal(1, tracker.KeyUpSuppressedCount);
    }

    [Fact]
    public void TabKeyDownAndKeyUp_ArePaired()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false, boundaryVirtualKey: VirtualKeys.Tab);

        Assert.True(interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Tab, PlatformKeyEventType.KeyDown, 15),
            GenuineMetadata()).ShouldSuppress);

        Assert.True(interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Tab, PlatformKeyEventType.KeyUp, 15),
            GenuineMetadata()).IsPhysicalKeyUpSuppression);
    }

    [Fact]
    public void EnterKeyDownAndKeyUp_ArePaired()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false, boundaryVirtualKey: VirtualKeys.Return);

        Assert.True(interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Return, PlatformKeyEventType.KeyDown, 28),
            GenuineMetadata()).ShouldSuppress);

        Assert.True(interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Return, PlatformKeyEventType.KeyUp, 28),
            GenuineMetadata()).IsPhysicalKeyUpSuppression);
    }

    [Fact]
    public void UnrelatedKeyUp_PassesThrough()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false);

        interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyDown, 57),
            GenuineMetadata());

        var result = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Left, PlatformKeyEventType.KeyUp, 75),
            GenuineMetadata());

        Assert.False(result.ShouldSuppress);
    }

    [Fact]
    public void InjectedKeyUp_PassesThroughWithoutMatching()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false);

        interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyDown, 57),
            GenuineMetadata());

        var result = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyUp, 57),
            InjectedMetadata());

        Assert.False(result.ShouldSuppress);
        Assert.Equal(BoundaryPairingState.AwaitingPhysicalKeyUp, tracker.State);
    }

    [Fact]
    public void InjectedKeyDown_DoesNotRegisterPairing()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false);

        var result = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyDown, 57),
            InjectedMetadata());

        Assert.False(result.ShouldSuppress);
        Assert.Equal(BoundaryPairingState.Idle, tracker.State);
    }

    [Fact]
    public void KeyUpAfterReinjection_DoesNotMatchExpiredRegistration()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false);

        interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyDown, 57),
            GenuineMetadata());

        interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyUp, 57),
            GenuineMetadata());

        var secondKeyUp = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyUp, 57),
            GenuineMetadata());

        Assert.False(secondKeyUp.ShouldSuppress);
    }

    [Fact]
    public async Task ExactlyOneDeliveredBoundaryPair_PerCorrectionCycle()
    {
        var events = new List<string>();
        var delivery = new FakeBoundaryDeliveryService(events);

        var engine = BoundaryOrderingTestHelpers.CreateEngine(
            new OrderingFakeReplacementService(true, events),
            delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Single(delivery.DeliveredBoundaries);
    }

    [Fact]
    public void KeyUpAfterReinjection_StillSuppressesHeldPhysicalRelease()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, replacementActive: false);

        interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyDown, 57),
            GenuineMetadata());

        // Injected delivery KeyUp passes through the observation filter and does not clear pairing.
        Assert.False(interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyUp, 57),
            InjectedMetadata()).ShouldSuppress);

        Assert.True(interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Space, PlatformKeyEventType.KeyUp, 57),
            GenuineMetadata()).IsPhysicalKeyUpSuppression);
    }

    [Fact]
    public async Task ReplacementFailure_StillDeliversExactlyOneBoundaryPair()
    {
        var events = new List<string>();
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(
            new OrderingFakeReplacementService(success: false, events),
            delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal(["replace", "deliver"], events);
    }

    [Fact]
    public async Task ReplacementTimeout_StillDeliversExactlyOneBoundaryPair()
    {
        var events = new List<string>();
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(
            new SlowReplacementService(TimeSpan.FromSeconds(5)),
            delivery,
            options: new LayoutCorrectionOptions { ReplacementTimeoutMilliseconds = 50 });

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Contains("deliver", events);
    }

    [Fact]
    public async Task ReplacementAbort_StillDeliversExactlyOneBoundaryPair()
    {
        var events = new List<string>();
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(
            new OrderingFakeReplacementService(success: false, events),
            delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Tab, 15)));

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal(VirtualKeys.Tab, delivery.DeliveredBoundaries[0].VirtualKeyCode);
    }

    private static WindowsBoundaryKeyInterceptor CreateInterceptor(
        BoundaryKeyPairingTracker tracker,
        bool replacementActive,
        int boundaryVirtualKey = VirtualKeys.Space)
    {
        return new WindowsBoundaryKeyInterceptor(
            new WindowsInputObservationFilter(),
            new FixedBoundaryResolver(boundaryVirtualKey),
            new AlwaysSuppressGate(),
            new FakeReplacementSessionNotifier(replacementActive),
            tracker);
    }

    private static KeyboardObservationEventArgs CreateObservation(
        int virtualKeyCode,
        PlatformKeyEventType eventType,
        int scanCode)
    {
        return new KeyboardObservationEventArgs
        {
            VirtualKeyCode = virtualKeyCode,
            ScanCode = scanCode,
            EventType = eventType,
        };
    }

    private static KeyboardHookMetadata GenuineMetadata()
    {
        return new KeyboardHookMetadata(0, 0);
    }

    private static KeyboardHookMetadata InjectedMetadata()
    {
        return new KeyboardHookMetadata(KeyboardHookFlags.Injected, 0);
    }

    private sealed class FixedBoundaryResolver(int virtualKeyCode) : IKeyboardCharacterResolver
    {
        public KeyboardCharacterResolution Resolve(KeyboardObservationEventArgs observation)
        {
            if (observation.EventType != PlatformKeyEventType.KeyDown)
            {
                return KeyboardCharacterResolution.Ignored();
            }

            return KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(virtualKeyCode, observation.ScanCode));
        }
    }

    private sealed class AlwaysSuppressGate : ILiveLayoutBoundaryGate
    {
        public bool ShouldSuppressPendingBoundary() => true;

        public PreparedLayoutCorrection? ObserveKeyDown(KeyboardCharacterResolution resolution)
        {
            return resolution.Kind == CharacterResolutionKind.WordBoundary
                ? new PreparedLayoutCorrection { OriginalText = "ghbdtn", ReplacementText = "привет" }
                : null;
        }
    }

    private sealed class FakeReplacementSessionNotifier(bool isActive) : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => isActive;

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}

public class BoundaryKeyUpBeforeReplacementTests
{
    [Fact]
    public async Task KeyUpCanBeSuppressedWhileReplacementIsStillRunning()
    {
        var events = new List<string>();
        var replacementActive = new MutableReplacementSessionNotifier();
        var tracker = new BoundaryKeyPairingTracker(
            replacementSessionNotifier: replacementActive);
        var interceptor = new WindowsBoundaryKeyInterceptor(
            new WindowsInputObservationFilter(),
            new SpaceBoundaryResolver(),
            new AlwaysSuppressGate(),
            replacementActive,
            tracker);

        var slowReplacement = new SlowReplacementService(TimeSpan.FromMilliseconds(100));
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(slowReplacement, delivery);

        // Realistic order: type the token, then suppress the boundary KeyDown.
        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");

        var keyDown = interceptor.TryIntercept(
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = VirtualKeys.Space,
                ScanCode = 57,
                EventType = PlatformKeyEventType.KeyDown,
            },
            new KeyboardHookMetadata(0, 0));

        Assert.True(keyDown.ShouldSuppress);
        Assert.NotNull(keyDown.Boundary);
        Assert.Equal(BoundaryPairingState.AwaitingPhysicalKeyUp, tracker.State);

        replacementActive.IsReplacementActive = true;
        var replacementTask = engine.ProcessInputAsync(TokenInputEvent.Boundary(
            keyDown.Boundary,
            keyDown.PreparedCorrection));

        Assert.True(interceptor.TryIntercept(
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = VirtualKeys.Space,
                ScanCode = 57,
                EventType = PlatformKeyEventType.KeyUp,
            },
            new KeyboardHookMetadata(0, 0)).IsPhysicalKeyUpSuppression);

        replacementActive.IsReplacementActive = false;
        await replacementTask;

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal(BoundaryPairingState.Idle, tracker.State);
    }

    [Fact]
    public void InjectedEventsDuringReplacement_DoNotAbortSession()
    {
        var state = new Platform.Abstractions.Input.TextReplacementSessionState();

        using (state.BeginSession())
        {
            state.NotifyKeyboardEvent(PlatformKeyEventType.KeyDown, isInjectedInput: true);
            Assert.False(state.AbortedByUserInput);
        }
    }

    private sealed class SpaceBoundaryResolver : IKeyboardCharacterResolver
    {
        public KeyboardCharacterResolution Resolve(KeyboardObservationEventArgs observation)
        {
            if (observation.EventType != PlatformKeyEventType.KeyDown)
            {
                return KeyboardCharacterResolution.Ignored();
            }

            return KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, observation.ScanCode));
        }
    }

    private sealed class AlwaysSuppressGate : ILiveLayoutBoundaryGate
    {
        public bool ShouldSuppressPendingBoundary() => true;

        public PreparedLayoutCorrection? ObserveKeyDown(KeyboardCharacterResolution resolution)
        {
            return resolution.Kind == CharacterResolutionKind.WordBoundary
                ? new PreparedLayoutCorrection { OriginalText = "ghbdtn", ReplacementText = "привет" }
                : null;
        }
    }

    private sealed class FakeReplacementSessionNotifier(bool replacementActive) : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => replacementActive;

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }

    private sealed class MutableReplacementSessionNotifier : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive { get; set; }

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}
