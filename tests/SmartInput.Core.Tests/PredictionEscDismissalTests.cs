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

public class PredictionEscDismissalEngineTests
{
    [Fact]
    public void TryDismissOverlaySuggestion_ClearsSuggestionAndSetsDismissedState()
    {
        var engine = CreateEngineWithSuggestion();

        Assert.True(engine.TryDismissOverlaySuggestion());

        var status = engine.Status;
        Assert.False(status.HasSuggestion);
        Assert.True(status.IsOverlayDismissedForCurrentContext);
        Assert.Equal(LivePredictionAction.SuggestionDismissed, status.LastAction);
        Assert.False(engine.TryGetOverlaySnapshot(out _));
    }

    [Fact]
    public void DismissedState_PersistsWithoutContextChange()
    {
        var engine = CreateEngineWithSuggestion();
        engine.TryDismissOverlaySuggestion();

        Assert.True(engine.IsOverlayDismissedForCurrentContext);
        Assert.False(engine.Status.HasSuggestion);
        Assert.False(engine.TryGetOverlaySnapshot(out _));
    }

    [Fact]
    public async Task NewGenuineText_ClearsDismissedStateAndAllowsSuggestion()
    {
        var engine = CreateEngineWithSuggestion();
        engine.TryDismissOverlaySuggestion();

        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('z'));

        Assert.False(engine.IsOverlayDismissedForCurrentContext);
    }

    [Fact]
    public void NotifyApplicationContextChanged_ClearsDismissedState()
    {
        var engine = CreateEngineWithSuggestion();
        engine.TryDismissOverlaySuggestion();

        engine.NotifyApplicationContextChanged();

        Assert.False(engine.IsOverlayDismissedForCurrentContext);
    }

    [Fact]
    public void ResetBuffer_ClearsDismissedState()
    {
        var engine = CreateEngineWithSuggestion();
        engine.TryDismissOverlaySuggestion();

        engine.ResetBuffer("manual");

        Assert.False(engine.IsOverlayDismissedForCurrentContext);
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

public class WindowsPredictionEscInterceptorTests
{
    [Fact]
    public void PlainEscKeyDown_WithGateOpen_SuppressesAndPairsKeyUp()
    {
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, gateOpen: true);

        var keyDown = CreateObservation(VirtualKeys.Escape, 1, PlatformKeyEventType.KeyDown);
        var result = interceptor.TryIntercept(keyDown, CreateMetadata());

        Assert.True(result.ShouldSuppress);
        Assert.True(result.IsEscDismissal);

        var keyUp = CreateObservation(VirtualKeys.Escape, 1, PlatformKeyEventType.KeyUp);
        var keyUpResult = interceptor.TryIntercept(keyUp, CreateMetadata());

        Assert.True(keyUpResult.ShouldSuppress);
        Assert.Equal(1, tracker.KeyUpSuppressedCount);
    }

    [Fact]
    public void IsPlainEscape_ModifiedCombinations_ReturnFalse()
    {
        var observation = CreateObservation(VirtualKeys.Escape, 1, PlatformKeyEventType.KeyDown);

        Assert.False(WindowsPredictionEscInterceptor.IsPlainEscape(observation, vk => vk == VirtualKeys.Control));
        Assert.False(WindowsPredictionEscInterceptor.IsPlainEscape(observation, vk => vk == VirtualKeys.Shift));
        Assert.False(WindowsPredictionEscInterceptor.IsPlainEscape(observation, vk => vk == VirtualKeys.Menu));
        Assert.False(WindowsPredictionEscInterceptor.IsPlainEscape(observation, vk => vk == VirtualKeys.LWin));
    }

    [Fact]
    public void PlainEscKeyDown_GateClosed_PassesThrough()
    {
        var interceptor = CreateInterceptor(new BoundaryKeyPairingTracker(), gateOpen: false);

        var result = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Escape, 1, PlatformKeyEventType.KeyDown),
            CreateMetadata());

        Assert.False(result.ShouldSuppress);
    }

    [Fact]
    public void InjectedEscKeyDown_PassesThrough()
    {
        var interceptor = CreateInterceptor(new BoundaryKeyPairingTracker(), gateOpen: true);
        var metadata = new KeyboardHookMetadata(
            0x10,
            SmartInputInjectionMarkers.SmartInputExtraInfo);

        var result = interceptor.TryIntercept(
            CreateObservation(VirtualKeys.Escape, 1, PlatformKeyEventType.KeyDown),
            metadata);

        Assert.False(result.ShouldSuppress);
    }

    private static WindowsPredictionEscInterceptor CreateInterceptor(
        BoundaryKeyPairingTracker tracker,
        bool gateOpen)
    {
        return new WindowsPredictionEscInterceptor(
            new WindowsInputObservationFilter(),
            new FakeGate(gateOpen),
            new FakeReplacementSessionNotifier(),
            tracker);
    }

    private static KeyboardObservationEventArgs CreateObservation(
        int virtualKey,
        int scanCode,
        PlatformKeyEventType eventType)
    {
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

    private sealed class FakeGate(bool open) : IPredictionEscDismissalGate
    {
        public bool ShouldInterceptPlainEsc() => open;
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

public class LivePredictionEscDismissalGateTests
{
    [Fact]
    public void ShouldInterceptPlainEsc_WhenOverlayVisibleAndAllowed_ReturnsTrue()
    {
        var gate = CreateGate(
            overlayVisible: true,
            hasSuggestion: true,
            tabAcceptanceInProgress: false,
            policy: AllowedPolicy(),
            protectionEnabled: true,
            predictionEnabled: true,
            emergencyPaused: false);

        Assert.True(gate.ShouldInterceptPlainEsc());
    }

    [Fact]
    public void ShouldInterceptPlainEsc_WhenTabAcceptanceInProgress_ReturnsFalse()
    {
        var gate = CreateGate(
            overlayVisible: true,
            hasSuggestion: true,
            tabAcceptanceInProgress: true,
            policy: AllowedPolicy(),
            protectionEnabled: true,
            predictionEnabled: true,
            emergencyPaused: false);

        Assert.False(gate.ShouldInterceptPlainEsc());
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    public void ShouldInterceptPlainEsc_BlockedPolicy_ReturnsFalse(AutomationPolicyState state)
    {
        var gate = CreateGate(
            overlayVisible: true,
            hasSuggestion: true,
            tabAcceptanceInProgress: false,
            policy: new AutomationPolicyResult
            {
                State = state,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
            },
            protectionEnabled: true,
            predictionEnabled: true,
            emergencyPaused: false);

        Assert.False(gate.ShouldInterceptPlainEsc());
    }

    private static LivePredictionEscDismissalGate CreateGate(
        bool overlayVisible,
        bool hasSuggestion,
        bool tabAcceptanceInProgress,
        AutomationPolicyResult policy,
        bool protectionEnabled,
        bool predictionEnabled,
        bool emergencyPaused)
    {
        return new LivePredictionEscDismissalGate(
            new FakeEngine(hasSuggestion, tabAcceptanceInProgress),
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

    private sealed class FakeEngine(bool hasSuggestion, bool tabAcceptanceInProgress) : ILivePredictionEngine
    {
        public LivePredictionStatus Status { get; } = new() { HasSuggestion = hasSuggestion };

        public bool IsTabAcceptanceInProgress => tabAcceptanceInProgress;

        public bool IsOverlayDismissedForCurrentContext => false;

        public event Action? OverlayStateChanged;

        public bool TryGetOverlaySnapshot(out LivePredictionOverlaySnapshot snapshot)
        {
            if (!hasSuggestion)
            {
                snapshot = null!;
                return false;
            }

            snapshot = new LivePredictionOverlaySnapshot
            {
                SuggestionText = "you",
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = 1,
            };

            return true;
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
