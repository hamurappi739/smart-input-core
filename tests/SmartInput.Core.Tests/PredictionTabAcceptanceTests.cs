using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Windows.Input;
using SmartInput.Platform.Windows.Services;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.Core.Tests;

public class PredictionTabAcceptanceEngineTests
{
    [Fact]
    public async Task TryBeginAcceptance_WithSuggestion_ReturnsAttemptOnce()
    {
        var engine = CreateEngineWithSuggestion();

        Assert.True(engine.TryBeginTabAcceptance(out var attempt));
        Assert.False(string.IsNullOrWhiteSpace(attempt.SuggestionText));
        Assert.False(engine.TryBeginTabAcceptance(out _));
    }

    [Fact]
    public async Task CompleteAcceptance_ClearsSuggestionAndUpdatesBuffer()
    {
        var engine = CreateEngineWithSuggestion();
        engine.TryBeginTabAcceptance(out var attempt);

        engine.CompleteTabAcceptance(attempt.Version);

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.Equal(LivePredictionAction.SuggestionAccepted, status.LastAction);
        Assert.False(engine.TryGetOverlaySnapshot(out _));
    }

    [Fact]
    public async Task AbortAcceptance_ClearsSuggestion()
    {
        var engine = CreateEngineWithSuggestion();
        engine.TryBeginTabAcceptance(out var attempt);

        engine.AbortTabAcceptance(attempt.Version);

        Assert.False(engine.Status.HasSuggestion);
        Assert.False(engine.TryGetOverlaySnapshot(out _));
    }

    private static LivePredictionEngine CreateEngineWithSuggestion()
    {
        var engine = new LivePredictionEngine(
            new PredictionService(new StarterLocalPredictionModel()),
            new FakeSettingsService(new AppSettings
            {
                IsEnabled = true,
                PredictionEnabled = true,
            }));

        engine.NotifyPolicyContextChanged(new AutomationPolicyResult
        {
            State = AutomationPolicyState.Allowed,
            AllowsAutomation = true,
            AllowsManualExternalTextOperations = true,
        });

        TypePhraseAsync(engine, "how are ").GetAwaiter().GetResult();
        return engine;
    }

    private static async Task TypePhraseAsync(LivePredictionEngine engine, string phrase)
    {
        foreach (var character in phrase)
        {
            if (char.IsLetter(character))
            {
                await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
            }
            else if (char.IsWhiteSpace(character))
            {
                await engine.ProcessInputAsync(TokenInputEvent.Boundary(
                    DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));
            }
        }
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            return Task.CompletedTask;
        }
    }
}

public class WindowsPredictionTabInterceptorTests
{
    [Fact]
    public void PlainTabKeyDown_WithGateOpen_SuppressesAndPairsKeyUp()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, gateOpen: true);

        var keyDown = CreateObservation(VirtualKeys.Tab, 15, PlatformKeyEventType.KeyDown);
        var result = interceptor.TryIntercept(keyDown, CreateMetadata());

        Assert.True(result.ShouldSuppress);
        Assert.True(result.IsTabAcceptance);

        var keyUp = CreateObservation(VirtualKeys.Tab, 15, PlatformKeyEventType.KeyUp);
        var keyUpResult = interceptor.TryIntercept(keyUp, CreateMetadata());

        Assert.True(keyUpResult.ShouldSuppress);
        Assert.Equal(1, tracker.KeyUpSuppressedCount);
    }

    [Fact]
    public void IsPlainTab_ModifiedCombinations_ReturnFalse()
    {
        var observation = CreateObservation(VirtualKeys.Tab, 15, PlatformKeyEventType.KeyDown);

        Assert.False(WindowsPredictionTabInterceptor.IsPlainTab(observation, vk => vk == VirtualKeys.Shift));
        Assert.False(WindowsPredictionTabInterceptor.IsPlainTab(observation, vk => vk == VirtualKeys.Control));
        Assert.False(WindowsPredictionTabInterceptor.IsPlainTab(observation, vk => vk == VirtualKeys.Menu));
        Assert.False(WindowsPredictionTabInterceptor.IsPlainTab(observation, vk => vk == VirtualKeys.LWin));
    }

    [Fact]
    public void PlainTabKeyDown_GateClosed_PassesThrough()
    {
        var interceptor = CreateInterceptor(new BoundaryKeyPairingTracker(), gateOpen: false);

        var result = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Tab, 15, PlatformKeyEventType.KeyDown),
            CreateMetadata());

        Assert.False(result.ShouldSuppress);
    }

    [Fact]
    public void InjectedTabKeyDown_PassesThrough()
    {
        var interceptor = CreateInterceptor(new BoundaryKeyPairingTracker(), gateOpen: true);
        var metadata = new KeyboardHookMetadata(
            0x10,
            SmartInputInjectionMarkers.SmartInputExtraInfo);

        var result = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Tab, 15, PlatformKeyEventType.KeyDown),
            metadata);

        Assert.False(result.ShouldSuppress);
    }

    private static WindowsPredictionTabInterceptor CreateInterceptor(
        BoundaryKeyPairingTracker tracker,
        bool gateOpen)
    {
        return new WindowsPredictionTabInterceptor(
            new WindowsInputObservationFilter(),
            new FakeGate(gateOpen),
            new FakeReplacementSessionNotifier(),
            tracker);
    }

    private static KeyboardObservationEventArgs CreateObservation(
        int virtualKey,
        int scanCode,
        PlatformKeyEventType eventType,
        bool shiftDown = false)
    {
        _ = shiftDown;
        return new KeyboardObservationEventArgs
        {
            VirtualKeyCode = virtualKey,
            ScanCode = scanCode,
            EventType = eventType,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    private static KeyboardHookMetadata CreateMetadata()
    {
        return new KeyboardHookMetadata(0, 0);
    }

    private sealed class AllowAllObservationFilter : IInputObservationFilter
    {
        public bool ShouldObserve(KeyboardHookMetadata metadata) => true;
    }

    private sealed class FakeGate(bool open) : IPredictionTabAcceptanceGate
    {
        public bool ShouldInterceptPlainTab() => open;
    }

    private sealed class FakeReplacementSessionNotifier : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => false;

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}

public class LivePredictionTabAcceptanceGateTests
{
    [Fact]
    public void ShouldInterceptPlainTab_WhenOverlayVisibleAndAllowed_ReturnsTrue()
    {
        var gate = CreateGate(
            overlayVisible: true,
            hasSuggestion: true,
            policy: AllowedPolicy(),
            protectionEnabled: true,
            predictionEnabled: true,
            emergencyPaused: false);

        Assert.True(gate.ShouldInterceptPlainTab());
    }

    [Fact]
    public void ShouldInterceptPlainTab_WhenOverlayHidden_ReturnsFalse()
    {
        var gate = CreateGate(
            overlayVisible: false,
            hasSuggestion: true,
            policy: AllowedPolicy(),
            protectionEnabled: true,
            predictionEnabled: true,
            emergencyPaused: false);

        Assert.False(gate.ShouldInterceptPlainTab());
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.SafeMode)]
    public void ShouldInterceptPlainTab_BlockedPolicy_ReturnsFalse(AutomationPolicyState state)
    {
        var gate = CreateGate(
            overlayVisible: true,
            hasSuggestion: true,
            policy: new AutomationPolicyResult
            {
                State = state,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = state == AutomationPolicyState.SafeMode,
            },
            protectionEnabled: true,
            predictionEnabled: true,
            emergencyPaused: false);

        Assert.False(gate.ShouldInterceptPlainTab());
    }

    private static LivePredictionTabAcceptanceGate CreateGate(
        bool overlayVisible,
        bool hasSuggestion,
        AutomationPolicyResult policy,
        bool protectionEnabled,
        bool predictionEnabled,
        bool emergencyPaused)
    {
        return new LivePredictionTabAcceptanceGate(
            new FakeEngine(hasSuggestion),
            new FakeOverlay(overlayVisible),
            new FakeSettingsService(protectionEnabled, predictionEnabled),
            new FakeSafety(policy),
            new FakeEmergencyPause(emergencyPaused),
            new FakeReplacementSessionNotifier());
    }

    private static AutomationPolicyResult AllowedPolicy()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.Allowed,
            AllowsAutomation = true,
            AllowsManualExternalTextOperations = true,
        };
    }

    private sealed class FakeEngine(bool hasSuggestion) : ILivePredictionEngine
    {
        public LivePredictionStatus Status { get; } = new() { HasSuggestion = hasSuggestion };

        public event Action? OverlayStateChanged;

        public bool TryGetOverlaySnapshot(out LivePredictionOverlaySnapshot snapshot)
        {
            snapshot = null!;
            return hasSuggestion;
        }

        public bool TryBeginTabAcceptance(out PredictionTabAcceptanceAttempt attempt)
        {
            attempt = null!;
            return false;
        }

        public void CompleteTabAcceptance(long version)
        {
        }

        public void AbortTabAcceptance(long version)
        {
        }

        public bool IsTabAcceptanceInProgress => false;

        public bool IsOverlayDismissedForCurrentContext => false;

        public bool TryDismissOverlaySuggestion() => false;

        public Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void ResetBuffer(string reason)
        {
        }

        public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
        {
        }

        public void NotifyApplicationContextChanged()
        {
        }
    }

    private sealed class FakeOverlay(bool visible) : IPredictionOverlayService
    {
        public PredictionOverlayStatus Status { get; } = new(
            visible ? PredictionOverlayVisibility.Visible : PredictionOverlayVisibility.Hidden,
            true);

        public Task ShowAsync(
            PredictionOverlayContent content,
            PredictionOverlayPlacement placement,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(
            PredictionOverlayContent content,
            PredictionOverlayPlacement placement,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettingsService(bool protectionEnabled, bool predictionEnabled) : ISettingsService
    {
        public AppSettings Current { get; } = new()
        {
            IsEnabled = protectionEnabled,
            PredictionEnabled = predictionEnabled,
        };

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSafety(AutomationPolicyResult policy) : IAutomationSafetyService
    {
        public AutomationPolicyResult EvaluateCurrentContext() => policy;

        public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(policy);

        public bool IsOperationAllowed(AutomationOperationKind operationKind) => policy.IsAllowed(operationKind);

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
            => Task.FromResult(policy.IsAllowed(operationKind));

        public string? GetBlockedReason(AutomationOperationKind operationKind)
            => policy.IsAllowed(operationKind) ? null : policy.Reason;
    }

    private sealed class FakeEmergencyPause(bool isPaused) : Platform.Abstractions.Safety.IEmergencyPauseService
    {
        public bool IsPaused { get; } = isPaused;

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }

    private sealed class FakeReplacementSessionNotifier : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => false;

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}
