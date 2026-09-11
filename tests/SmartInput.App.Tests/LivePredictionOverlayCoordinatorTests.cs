using SmartInput.App.Services;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Windows.Overlay;

namespace SmartInput.App.Tests;

public class LivePredictionOverlayCoordinatorTests
{
    [Fact]
    public async Task SyncOverlayAsync_WithSuggestion_ShowsOverlayNearCaret()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService();
        var caret = new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42));
        var coordinator = CreateCoordinator(engine, overlay, caret);

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.ShowCallCount);
        Assert.Equal(0, overlay.UpdateCallCount);
        Assert.Equal(PredictionOverlayVisibility.Visible, overlay.Status.Visibility);
        Assert.Equal(120, overlay.LastPlacement!.X);
        Assert.Equal(80, overlay.LastPlacement.Y);
        Assert.False(string.IsNullOrEmpty(overlay.LastContent!.SuggestionText));
    }

    [Fact]
    public async Task SyncOverlayAsync_SecondUpdate_UsesUpdateNotNewShow()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService();
        var caret = new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42));
        var coordinator = CreateCoordinator(engine, overlay, caret);

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();
        overlay.Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true);
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.ShowCallCount);
        Assert.Equal(1, overlay.UpdateCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_NoSuggestion_HidesOverlay()
    {
        var engine = CreateEngineWithoutSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var caret = new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42));
        var coordinator = CreateCoordinator(engine, overlay, caret);

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.SafeMode)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    public async Task SyncOverlayAsync_BlockedPolicy_HidesOverlay(AutomationPolicyState state)
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var safety = new FakeAutomationSafetyService(new AutomationPolicyResult
        {
            State = state,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = state == AutomationPolicyState.SafeMode,
        });
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)),
            safety: safety);

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
        Assert.Equal(0, overlay.ShowCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_ProtectionDisabled_HidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var settings = new FakeSettingsService(new AppSettings
        {
            IsEnabled = false,
            PredictionEnabled = true,
        });
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)),
            settings: settings);

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_PredictionDisabled_HidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var settings = new FakeSettingsService(new AppSettings
        {
            IsEnabled = true,
            PredictionEnabled = false,
        });
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)),
            settings: settings);

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_EmergencyPause_HidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var emergencyPause = new FakeEmergencyPauseService(isPaused: true);
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)),
            emergencyPause: emergencyPause);

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_MissingCaret_HidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(null));

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_InvalidCaret_HidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 0, 42)));

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_ExpiredSuggestion_HidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        engine.OverlayStateChanged += () => { };
        Assert.True(engine.TryGetOverlaySnapshot(out var snapshot));

        var expiredEngine = new ExpiringOverlayEngine(engine, new LivePredictionOverlaySnapshot
        {
            SuggestionText = snapshot!.SuggestionText,
            UpdatedAt = DateTimeOffset.UtcNow - LivePredictionOverlayCoordinator.SuggestionExpiry - TimeSpan.FromSeconds(1),
            Version = snapshot.Version,
        });

        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var coordinator = CreateCoordinator(
            expiredEngine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task OverlayStateChanged_TriggersAsyncSync()
    {
        var engine = CreateEngineWithoutSuggestion();
        var overlay = new RecordingPredictionOverlayService();
        var caret = new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42));
        var coordinator = CreateCoordinator(engine, overlay, caret);

        await coordinator.InitializeAsync();
        await TypePhraseAsync(engine, "how are ");
        await Task.Delay(100);

        Assert.True(overlay.ShowCallCount >= 1 || overlay.UpdateCallCount >= 1);
    }

    [Fact]
    public async Task DisposeAsync_ShutsDownOverlay()
    {
        var engine = CreateEngineWithoutSuggestion();
        var overlay = new RecordingPredictionOverlayService();
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(1, overlay.ShutdownCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_DoesNotPersistSuggestionTextInOverlayStatus()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService();
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        var statusProperties = typeof(PredictionOverlayStatus)
            .GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.DoesNotContain(typeof(string), statusProperties);
    }

    [Fact]
    public async Task SyncOverlayAsync_CaretWindowMismatch_HidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService
        {
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true),
        };
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 99)),
            activeApplication: new FakeActiveApplicationService(new ActiveApplicationInfo(
                "notepad",
                "Untitled - Notepad",
                "Notepad",
                42)));

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task SyncOverlayAsync_MultipleUpdates_ReuseSingleOverlayWindow()
    {
        var engine = CreateEngineWithSuggestion();
        var overlay = new RecordingPredictionOverlayService();
        var coordinator = CreateCoordinator(
            engine,
            overlay,
            new FakeCaretPositionService(new CaretScreenPosition(120, 80, 2, 18, 42)));

        await coordinator.InitializeAsync();
        await coordinator.SyncOverlayAsync();
        overlay.Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true);
        await coordinator.SyncOverlayAsync();
        await coordinator.SyncOverlayAsync();

        Assert.Equal(1, overlay.ShowCallCount);
        Assert.Equal(2, overlay.UpdateCallCount);
        Assert.Equal(1, overlay.WindowCreateCount);
    }

    [Fact]
    public void WindowsOverlayService_UsesNonActivatingTransparentStyles()
    {
        var styles = PredictionOverlayWindowStyles.ExtendedStyles;
        Assert.True((styles & PredictionOverlayWindowStyles.WsExNoActivate) != 0);
        Assert.True((styles & PredictionOverlayWindowStyles.WsExTransparent) != 0);
        Assert.True((styles & PredictionOverlayWindowStyles.WsExTopmost) != 0);
        Assert.True((styles & PredictionOverlayWindowStyles.WsExLayered) != 0);
    }

    private static LivePredictionOverlayCoordinator CreateCoordinator(
        ILivePredictionEngine engine,
        RecordingPredictionOverlayService overlay,
        FakeCaretPositionService caret,
        FakeAutomationSafetyService? safety = null,
        FakeSettingsService? settings = null,
        FakeEmergencyPauseService? emergencyPause = null,
        FakeActiveApplicationService? activeApplication = null)
    {
        return new LivePredictionOverlayCoordinator(
            engine,
            overlay,
            caret,
            activeApplication ?? new FakeActiveApplicationService(new ActiveApplicationInfo(
                "notepad",
                "Untitled - Notepad",
                "Notepad",
                42)),
            safety ?? new FakeAutomationSafetyService(AllowedPolicy()),
            emergencyPause ?? new FakeEmergencyPauseService(false),
            settings ?? new FakeSettingsService(EnabledSettings()),
            SmartInput.Core.Diagnostics.NullPerformanceMetricsRecorder.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LivePredictionOverlayCoordinator>.Instance);
    }

    private static LivePredictionEngine CreateEngineWithSuggestion()
    {
        var engine = CreateEngineWithoutSuggestion();
        TypePhraseAsync(engine, "how are ").GetAwaiter().GetResult();
        return engine;
    }

    private static LivePredictionEngine CreateEngineWithoutSuggestion()
    {
        var engine = new LivePredictionEngine(
            new PredictionService(new StarterLocalPredictionModel()),
            new FakeSettingsService(EnabledSettings()));

        engine.NotifyPolicyContextChanged(AllowedPolicy());
        return engine;
    }

    private static AppSettings EnabledSettings()
    {
        return new AppSettings
        {
            IsEnabled = true,
            PredictionEnabled = true,
        };
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

    private static async Task TypePhraseAsync(ILivePredictionEngine engine, string phrase)
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

    private sealed class RecordingPredictionOverlayService : IPredictionOverlayService
    {
        public int ShowCallCount { get; private set; }

        public int UpdateCallCount { get; private set; }

        public int HideCallCount { get; private set; }

        public int ShutdownCallCount { get; private set; }

        public int WindowCreateCount { get; private set; }

        public PredictionOverlayContent? LastContent { get; private set; }

        public PredictionOverlayPlacement? LastPlacement { get; private set; }

        public PredictionOverlayStatus Status { get; set; } =
            new(PredictionOverlayVisibility.Hidden, false);

        public Task ShowAsync(
            PredictionOverlayContent content,
            PredictionOverlayPlacement placement,
            CancellationToken cancellationToken = default)
        {
            ShowCallCount++;
            WindowCreateCount = Math.Max(1, WindowCreateCount);
            LastContent = content;
            LastPlacement = placement;
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            PredictionOverlayContent content,
            PredictionOverlayPlacement placement,
            CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            LastContent = content;
            LastPlacement = placement;
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Visible, true);
            return Task.CompletedTask;
        }

        public Task HideAsync(CancellationToken cancellationToken = default)
        {
            HideCallCount++;
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Hidden, Status.IsWindowCreated);
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownCallCount++;
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Hidden, false);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCaretPositionService(CaretScreenPosition? position) : ICaretPositionService
    {
        public Task<CaretScreenPosition?> GetCaretScreenPositionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(position);
        }
    }

    private sealed class FakeActiveApplicationService(ActiveApplicationInfo info) : IActiveApplicationService
    {
        public Task<ActiveApplicationInfo?> GetActiveApplicationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ActiveApplicationInfo?>(info);
        }
    }

    private sealed class FakeAutomationSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
    {
        public AutomationPolicyResult EvaluateCurrentContext() => policy;

        public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policy);
        }

        public bool IsOperationAllowed(AutomationOperationKind operationKind) => policy.IsAllowed(operationKind);

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policy.IsAllowed(operationKind));
        }

        public string? GetBlockedReason(AutomationOperationKind operationKind)
        {
            return policy.IsAllowed(operationKind) ? null : policy.Reason;
        }
    }

    private sealed class FakeEmergencyPauseService(bool isPaused) : IEmergencyPauseService
    {
        public bool IsPaused { get; private set; } = isPaused;

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;
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

    private sealed class ExpiringOverlayEngine(ILivePredictionEngine inner, LivePredictionOverlaySnapshot snapshot)
        : ILivePredictionEngine
    {
        public LivePredictionStatus Status => inner.Status;

        public event Action? OverlayStateChanged
        {
            add => inner.OverlayStateChanged += value;
            remove => inner.OverlayStateChanged -= value;
        }

        public bool TryGetOverlaySnapshot(out LivePredictionOverlaySnapshot overlaySnapshot)
        {
            overlaySnapshot = snapshot;
            return true;
        }

        public bool TryBeginTabAcceptance(out PredictionTabAcceptanceAttempt attempt)
            => inner.TryBeginTabAcceptance(out attempt);

        public void CompleteTabAcceptance(long version) => inner.CompleteTabAcceptance(version);

        public void AbortTabAcceptance(long version) => inner.AbortTabAcceptance(version);

        public bool IsTabAcceptanceInProgress => inner.IsTabAcceptanceInProgress;

        public bool IsOverlayDismissedForCurrentContext => inner.IsOverlayDismissedForCurrentContext;

        public bool TryDismissOverlaySuggestion() => inner.TryDismissOverlaySuggestion();

        public Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default)
            => inner.ProcessInputAsync(input, cancellationToken);

        public void ResetBuffer(string reason) => inner.ResetBuffer(reason);

        public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
            => inner.NotifyPolicyContextChanged(policy);

        public void NotifyApplicationContextChanged() => inner.NotifyApplicationContextChanged();
    }
}
