using SmartInput.App.Services;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.App.Tests;

public class LivePredictionEscDismissalCoordinatorTests
{
    [Fact]
    public async Task ProcessEscDismissalAsync_Success_DismissesAndHidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var dismissal = new PredictionEscDismissalService(engine);
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var delivery = new RecordingBoundaryDeliveryService();
        var coordinator = CreateCoordinator(engine, dismissal, overlay, delivery);

        await coordinator.InitializeAsync();
        await coordinator.ProcessEscDismissalAsync(CreateEscObservation());

        Assert.Equal(1, overlay.HideCallCount);
        Assert.True(engine.IsOverlayDismissedForCurrentContext);
        Assert.False(engine.Status.HasSuggestion);
        Assert.Equal(0, delivery.DeliverCallCount);
    }

    [Fact]
    public async Task ProcessEscDismissalAsync_NoOverlayVisible_ReinjectsEsc()
    {
        var engine = CreateEngineWithSuggestion();
        var dismissal = new PredictionEscDismissalService(engine);
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Hidden, true) };
        var delivery = new RecordingBoundaryDeliveryService();
        var coordinator = CreateCoordinator(engine, dismissal, overlay, delivery);

        await coordinator.InitializeAsync();
        await coordinator.ProcessEscDismissalAsync(CreateEscObservation());

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.True(engine.Status.HasSuggestion);
    }

    [Fact]
    public async Task ProcessEscDismissalAsync_CaretMismatch_ReinjectsEsc()
    {
        var engine = CreateEngineWithSuggestion();
        var dismissal = new PredictionEscDismissalService(engine);
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var delivery = new RecordingBoundaryDeliveryService();
        var coordinator = CreateCoordinator(
            engine,
            dismissal,
            overlay,
            delivery,
            useNullCaret: true);

        await coordinator.InitializeAsync();
        await coordinator.ProcessEscDismissalAsync(CreateEscObservation());

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.True(engine.Status.HasSuggestion);
    }

    [Fact]
    public async Task ProcessEscDismissalAsync_BlockedPolicy_ReinjectsEsc()
    {
        var engine = CreateEngineWithSuggestion();
        var dismissal = new PredictionEscDismissalService(engine);
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var delivery = new RecordingBoundaryDeliveryService();
        var coordinator = CreateCoordinator(
            engine,
            dismissal,
            overlay,
            delivery,
            policy: new AutomationPolicyResult
            {
                State = AutomationPolicyState.SecureInput,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
            });

        await coordinator.InitializeAsync();
        await coordinator.ProcessEscDismissalAsync(CreateEscObservation());

        Assert.Equal(1, delivery.DeliverCallCount);
    }

    private static LivePredictionEscDismissalCoordinator CreateCoordinator(
        ILivePredictionEngine engine,
        IPredictionEscDismissalService dismissal,
        RecordingOverlayService overlay,
        RecordingBoundaryDeliveryService delivery,
        bool useNullCaret = false,
        AutomationPolicyResult? policy = null)
    {
        ICaretPositionService caretService = useNullCaret
            ? new FakeCaretService(null)
            : new FakeCaretService(new CaretScreenPosition(120, 80, 2, 18, 42));

        return new LivePredictionEscDismissalCoordinator(
            new FakeInputMonitor(),
            dismissal,
            overlay,
            caretService,
            new FakeActiveApplicationService(new ActiveApplicationInfo("notepad", "note", "Notepad", 42)),
            new FakeSafetyService(policy ?? AllowedPolicy()),
            new FakeEmergencyPauseService(false),
            new FakeSettingsService(new AppSettings { IsEnabled = true, PredictionEnabled = true }),
            new FakeReplacementSessionNotifier(),
            engine,
            delivery,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LivePredictionEscDismissalCoordinator>.Instance);
    }

    private static LivePredictionEngine CreateEngineWithSuggestion()
    {
        var engine = new LivePredictionEngine(
            new PredictionService(new StarterLocalPredictionModel()),
            new FakeSettingsService(new AppSettings { IsEnabled = true, PredictionEnabled = true }));

        engine.NotifyPolicyContextChanged(AllowedPolicy());
        TypePhraseAsync(engine, "how are ").GetAwaiter().GetResult();
        return engine;
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

    private static KeyboardObservationEventArgs CreateEscObservation()
    {
        return new KeyboardObservationEventArgs
        {
            VirtualKeyCode = VirtualKeys.Escape,
            ScanCode = 1,
            EventType = Platform.Abstractions.Input.KeyEventType.KeyDown,
            TimestampUtc = DateTimeOffset.UtcNow,
            IsPredictionEscDismissal = true,
        };
    }

    private sealed class RecordingOverlayService : IPredictionOverlayService
    {
        public int HideCallCount { get; private set; }

        public PredictionOverlayStatus Status { get; set; } =
            new(PredictionOverlayVisibility.Hidden, true);

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

        public Task HideAsync(CancellationToken cancellationToken = default)
        {
            HideCallCount++;
            Status = new PredictionOverlayStatus(PredictionOverlayVisibility.Hidden, Status.IsWindowCreated);
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingBoundaryDeliveryService : IBoundaryKeyDeliveryService
    {
        public int DeliverCallCount { get; private set; }

        public Task DeliverAsync(DeferredBoundaryKey boundary, CancellationToken cancellationToken = default)
        {
            DeliverCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInputMonitor : IInputMonitor
    {
        public bool IsMonitoring => false;

        public event EventHandler<KeyboardObservationEventArgs>? InputObserved;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCaretService(CaretScreenPosition? position) : ICaretPositionService
    {
        public Task<CaretScreenPosition?> GetCaretScreenPositionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(position);
    }

    private sealed class FakeActiveApplicationService(ActiveApplicationInfo info) : IActiveApplicationService
    {
        public Task<ActiveApplicationInfo?> GetActiveApplicationAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ActiveApplicationInfo?>(info);
    }

    private sealed class FakeSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
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

    private sealed class FakeEmergencyPauseService(bool isPaused) : IEmergencyPauseService
    {
        public bool IsPaused { get; } = isPaused;

        public void Pause()
        {
        }

        public void Resume()
        {
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

    private sealed class FakeReplacementSessionNotifier : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => false;

        public void NotifyKeyboardEventDuringReplacement(
            Platform.Abstractions.Input.KeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}
