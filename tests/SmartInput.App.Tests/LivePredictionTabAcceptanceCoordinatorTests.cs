using SmartInput.App.Services;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.App.Tests;

public class LivePredictionTabAcceptanceCoordinatorTests
{
    [Fact]
    public async Task ProcessTabAcceptanceAsync_Success_InsertsOnceAndHidesOverlay()
    {
        var engine = CreateEngineWithSuggestion();
        var acceptance = new PredictionTabAcceptanceService(engine);
        var replacement = new RecordingReplacementService();
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var coordinator = CreateCoordinator(engine, acceptance, replacement, overlay);

        await coordinator.InitializeAsync();
        await coordinator.ProcessTabAcceptanceAsync(CreateTabObservation());

        Assert.Equal(1, replacement.InsertCallCount + replacement.ReplaceCallCount);
        Assert.Equal(1, overlay.HideCallCount);
        Assert.False(engine.Status.HasSuggestion);
        Assert.Equal(0, replacement.DeliverCallCount);
    }

    [Fact]
    public async Task ProcessTabAcceptanceAsync_Failure_ReinjectsTabOnce()
    {
        var engine = CreateEngineWithSuggestion();
        var acceptance = new PredictionTabAcceptanceService(engine);
        var replacement = new RecordingReplacementService
        {
            InsertResult = TextReplacementResult.Failed("failed"),
        };
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var coordinator = CreateCoordinator(engine, acceptance, replacement, overlay);

        await coordinator.InitializeAsync();
        await coordinator.ProcessTabAcceptanceAsync(CreateTabObservation());

        Assert.Equal(1, replacement.DeliverCallCount);
        Assert.Equal(1, overlay.HideCallCount);
        Assert.False(engine.Status.HasSuggestion);
    }

    [Fact]
    public async Task ProcessTabAcceptanceAsync_NoOverlayVisible_ReinjectsTab()
    {
        var engine = CreateEngineWithSuggestion();
        var acceptance = new PredictionTabAcceptanceService(engine);
        var replacement = new RecordingReplacementService();
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Hidden, true) };
        var coordinator = CreateCoordinator(engine, acceptance, replacement, overlay);

        await coordinator.InitializeAsync();
        await coordinator.ProcessTabAcceptanceAsync(CreateTabObservation());

        Assert.Equal(1, replacement.DeliverCallCount);
        Assert.Equal(0, replacement.InsertCallCount + replacement.ReplaceCallCount);
    }

    [Fact]
    public async Task ProcessTabAcceptanceAsync_AbortedByUserInput_ReinjectsTab()
    {
        var engine = CreateEngineWithSuggestion();
        var acceptance = new PredictionTabAcceptanceService(engine);
        var replacement = new RecordingReplacementService
        {
            InsertResult = TextReplacementResult.AbortedByUserInput(0, 0),
        };
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var coordinator = CreateCoordinator(engine, acceptance, replacement, overlay);

        await coordinator.InitializeAsync();
        await coordinator.ProcessTabAcceptanceAsync(CreateTabObservation());

        Assert.Equal(1, replacement.DeliverCallCount);
        Assert.Equal(1, overlay.HideCallCount);
    }

    [Fact]
    public async Task ProcessTabAcceptanceAsync_CaretMismatch_ReinjectsTab()
    {
        var engine = CreateEngineWithSuggestion();
        var acceptance = new PredictionTabAcceptanceService(engine);
        var replacement = new RecordingReplacementService();
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var coordinator = CreateCoordinator(
            engine,
            acceptance,
            replacement,
            overlay,
            caret: null,
            useNullCaret: true);

        await coordinator.InitializeAsync();
        await coordinator.ProcessTabAcceptanceAsync(CreateTabObservation());

        Assert.Equal(1, replacement.DeliverCallCount);
        Assert.Equal(0, replacement.InsertCallCount + replacement.ReplaceCallCount);
    }

    [Fact]
    public async Task ProcessTabAcceptanceAsync_BlockedPolicy_ReinjectsTab()
    {
        var engine = CreateEngineWithSuggestion();
        var acceptance = new PredictionTabAcceptanceService(engine);
        var replacement = new RecordingReplacementService();
        var overlay = new RecordingOverlayService { Status = new(PredictionOverlayVisibility.Visible, true) };
        var coordinator = CreateCoordinator(
            engine,
            acceptance,
            replacement,
            overlay,
            policy: new AutomationPolicyResult
            {
                State = AutomationPolicyState.SecureInput,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
            });

        await coordinator.InitializeAsync();
        await coordinator.ProcessTabAcceptanceAsync(CreateTabObservation());

        Assert.Equal(1, replacement.DeliverCallCount);
    }

    private static readonly CaretScreenPosition DefaultCaret = new(120, 80, 2, 18, 42);

    private static LivePredictionTabAcceptanceCoordinator CreateCoordinator(
        ILivePredictionEngine engine,
        IPredictionTabAcceptanceService acceptance,
        RecordingReplacementService replacement,
        RecordingOverlayService overlay,
        CaretScreenPosition? caret = null,
        bool useNullCaret = false,
        AutomationPolicyResult? policy = null)
    {
        var caretService = useNullCaret
            ? new FakeCaretService(null)
            : new FakeCaretService(caret ?? DefaultCaret);
        return new LivePredictionTabAcceptanceCoordinator(
            new FakeInputMonitor(),
            acceptance,
            replacement,
            replacement,
            overlay,
            caretService,
            new FakeActiveApplicationService(new ActiveApplicationInfo("notepad", "note", "Notepad", 42)),
            new FakeSafetyService(policy ?? AllowedPolicy()),
            new FakeEmergencyPauseService(false),
            new FakeSettingsService(new AppSettings { IsEnabled = true, PredictionEnabled = true }),
            new FakeReplacementSessionNotifier(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LivePredictionTabAcceptanceCoordinator>.Instance);
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

    private static KeyboardObservationEventArgs CreateTabObservation()
    {
        return new KeyboardObservationEventArgs
        {
            VirtualKeyCode = VirtualKeys.Tab,
            ScanCode = 15,
            EventType = Platform.Abstractions.Input.KeyEventType.KeyDown,
            TimestampUtc = DateTimeOffset.UtcNow,
            IsPredictionTabAcceptance = true,
        };
    }

    private sealed class RecordingReplacementService
        : ISafeTextReplacementService, IBoundaryKeyDeliveryService
    {
        public int InsertCallCount { get; private set; }

        public int ReplaceCallCount { get; private set; }

        public int DeliverCallCount { get; private set; }

        public TextReplacementResult InsertResult { get; init; } =
            TextReplacementResult.Success(0, 3);

        public TextReplacementResult ReplaceResult { get; init; } =
            TextReplacementResult.Success(3, 6);

        public Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
        {
            InsertCallCount++;
            return Task.FromResult(InsertResult);
        }

        public Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            ReplaceCallCount++;
            return Task.FromResult(ReplaceResult);
        }

        public Task DeliverAsync(DeferredBoundaryKey boundary, CancellationToken cancellationToken = default)
        {
            DeliverCallCount++;
            return Task.CompletedTask;
        }

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
            => InsertTextAsync("xyz", cancellationToken);
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
