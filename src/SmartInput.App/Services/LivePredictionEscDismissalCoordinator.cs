using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.App.Services;

public interface ILivePredictionEscDismissalCoordinator
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task ProcessEscDismissalAsync(KeyboardObservationEventArgs observation);
}

public sealed class LivePredictionEscDismissalCoordinator
    : ILivePredictionEscDismissalCoordinator, IAsyncDisposable
{
    private readonly IInputMonitor _inputMonitor;
    private readonly IPredictionEscDismissalService _dismissalService;
    private readonly IPredictionOverlayService _overlayService;
    private readonly ICaretPositionService _caretPositionService;
    private readonly IActiveApplicationService _activeApplicationService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ISettingsService _settingsService;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly ILivePredictionEngine _predictionEngine;
    private readonly IBoundaryKeyDeliveryService _boundaryKeyDeliveryService;
    private readonly ILogger<LivePredictionEscDismissalCoordinator> _logger;

    private bool _initialized;
    private bool _disposed;

    public LivePredictionEscDismissalCoordinator(
        IInputMonitor inputMonitor,
        IPredictionEscDismissalService dismissalService,
        IPredictionOverlayService overlayService,
        ICaretPositionService caretPositionService,
        IActiveApplicationService activeApplicationService,
        IAutomationSafetyService automationSafetyService,
        IEmergencyPauseService emergencyPauseService,
        ISettingsService settingsService,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        ILivePredictionEngine predictionEngine,
        IBoundaryKeyDeliveryService boundaryKeyDeliveryService,
        ILogger<LivePredictionEscDismissalCoordinator> logger)
    {
        _inputMonitor = inputMonitor;
        _dismissalService = dismissalService;
        _overlayService = overlayService;
        _caretPositionService = caretPositionService;
        _activeApplicationService = activeApplicationService;
        _automationSafetyService = automationSafetyService;
        _emergencyPauseService = emergencyPauseService;
        _settingsService = settingsService;
        _replacementSessionNotifier = replacementSessionNotifier;
        _predictionEngine = predictionEngine;
        _boundaryKeyDeliveryService = boundaryKeyDeliveryService;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _inputMonitor.InputObserved += OnInputObserved;
        _initialized = true;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inputMonitor.InputObserved -= OnInputObserved;
        await _overlayService.HideAsync().ConfigureAwait(false);
    }

    private void OnInputObserved(object? sender, KeyboardObservationEventArgs observation)
    {
        if (!observation.IsPredictionEscDismissal)
        {
            return;
        }

        _ = ProcessEscDismissalAsync(observation);
    }

    public async Task ProcessEscDismissalAsync(KeyboardObservationEventArgs observation)
    {
        if (_disposed || observation.EventType != Platform.Abstractions.Input.KeyEventType.KeyDown)
        {
            return;
        }

        try
        {
            if (!await ValidateDismissalContextAsync().ConfigureAwait(false))
            {
                await ReinjectEscapeAsync(observation).ConfigureAwait(false);
                return;
            }

            if (!_dismissalService.DismissVisibleSuggestion())
            {
                await ReinjectEscapeAsync(observation).ConfigureAwait(false);
                return;
            }

            await _overlayService.HideAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prediction Esc dismissal failed.");
            await ReinjectEscapeAsync(observation).ConfigureAwait(false);
        }
    }

    private async Task<bool> ValidateDismissalContextAsync()
    {
        if (_replacementSessionNotifier.IsReplacementActive)
        {
            return false;
        }

        if (_predictionEngine.IsTabAcceptanceInProgress)
        {
            return false;
        }

        if (_emergencyPauseService.IsPaused)
        {
            return false;
        }

        var settings = _settingsService.Current;
        if (!settings.IsEnabled || !settings.PredictionEnabled)
        {
            return false;
        }

        if (_overlayService.Status.Visibility != PredictionOverlayVisibility.Visible)
        {
            return false;
        }

        if (!_predictionEngine.TryGetOverlaySnapshot(out _))
        {
            return false;
        }

        var policy = await _automationSafetyService
            .EvaluateCurrentContextAsync()
            .ConfigureAwait(false);

        if (policy.State != AutomationPolicyState.Allowed
            || !policy.AllowsAutomation
            || policy.IsEmergencyPaused)
        {
            return false;
        }

        var activeApplication = await _activeApplicationService
            .GetActiveApplicationAsync()
            .ConfigureAwait(false);

        var foregroundHandle = activeApplication?.WindowHandle ?? 0;
        if (foregroundHandle == 0)
        {
            return false;
        }

        var caret = await _caretPositionService
            .GetCaretScreenPositionAsync()
            .ConfigureAwait(false);

        return caret is not null
            && caret.WindowHandle != 0
            && caret.CaretHeight > 0
            && caret.WindowHandle == foregroundHandle;
    }

    private Task ReinjectEscapeAsync(KeyboardObservationEventArgs observation)
    {
        var escapeKey = DeferredBoundaryKey.FromVirtualKey(
            observation.VirtualKeyCode,
            observation.ScanCode);

        return _boundaryKeyDeliveryService.DeliverAsync(escapeKey);
    }
}
