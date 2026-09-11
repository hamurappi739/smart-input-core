using Microsoft.Extensions.Logging;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.App.Services;

public interface ILivePredictionTabAcceptanceCoordinator
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task ProcessTabAcceptanceAsync(KeyboardObservationEventArgs observation);
}

public sealed class LivePredictionTabAcceptanceCoordinator
    : ILivePredictionTabAcceptanceCoordinator, IAsyncDisposable
{
    internal static readonly TimeSpan AcceptanceTimeout = TimeSpan.FromSeconds(2);

    private readonly IInputMonitor _inputMonitor;
    private readonly IPredictionTabAcceptanceService _acceptanceService;
    private readonly ISafeTextReplacementService _safeTextReplacementService;
    private readonly IBoundaryKeyDeliveryService _boundaryKeyDeliveryService;
    private readonly IPredictionOverlayService _overlayService;
    private readonly ICaretPositionService _caretPositionService;
    private readonly IActiveApplicationService _activeApplicationService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ISettingsService _settingsService;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly ILogger<LivePredictionTabAcceptanceCoordinator> _logger;
    private readonly SemaphoreSlim _acceptanceGate = new(1, 1);

    private nint _lastForegroundWindowHandle;
    private bool _initialized;
    private bool _disposed;

    public LivePredictionTabAcceptanceCoordinator(
        IInputMonitor inputMonitor,
        IPredictionTabAcceptanceService acceptanceService,
        ISafeTextReplacementService safeTextReplacementService,
        IBoundaryKeyDeliveryService boundaryKeyDeliveryService,
        IPredictionOverlayService overlayService,
        ICaretPositionService caretPositionService,
        IActiveApplicationService activeApplicationService,
        IAutomationSafetyService automationSafetyService,
        IEmergencyPauseService emergencyPauseService,
        ISettingsService settingsService,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        ILogger<LivePredictionTabAcceptanceCoordinator> logger)
    {
        _inputMonitor = inputMonitor;
        _acceptanceService = acceptanceService;
        _safeTextReplacementService = safeTextReplacementService;
        _boundaryKeyDeliveryService = boundaryKeyDeliveryService;
        _overlayService = overlayService;
        _caretPositionService = caretPositionService;
        _activeApplicationService = activeApplicationService;
        _automationSafetyService = automationSafetyService;
        _emergencyPauseService = emergencyPauseService;
        _settingsService = settingsService;
        _replacementSessionNotifier = replacementSessionNotifier;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _inputMonitor.InputObserved += OnInputObserved;
        var activeApplication = await _activeApplicationService
            .GetActiveApplicationAsync(cancellationToken)
            .ConfigureAwait(false);
        _lastForegroundWindowHandle = activeApplication?.WindowHandle ?? 0;
        _initialized = true;
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
        _acceptanceGate.Dispose();
    }

    private void OnInputObserved(object? sender, KeyboardObservationEventArgs observation)
    {
        if (!observation.IsPredictionTabAcceptance)
        {
            return;
        }

        _ = ProcessTabAcceptanceAsync(observation);
    }

    public async Task ProcessTabAcceptanceAsync(KeyboardObservationEventArgs observation)
    {
        if (_disposed || observation.EventType != Platform.Abstractions.Input.KeyEventType.KeyDown)
        {
            return;
        }

        if (!await _acceptanceGate.WaitAsync(0).ConfigureAwait(false))
        {
            await ReinjectTabAsync(observation).ConfigureAwait(false);
            return;
        }

        var tabBoundary = DeferredBoundaryKey.FromVirtualKey(
            observation.VirtualKeyCode,
            observation.ScanCode);
        long acceptanceVersion = 0;
        var acceptanceStarted = false;

        try
        {
            if (!await ValidateAcceptanceContextAsync().ConfigureAwait(false))
            {
                await ReinjectTabAsync(observation).ConfigureAwait(false);
                return;
            }

            if (!_acceptanceService.TryBeginAcceptance(out var attempt))
            {
                await ReinjectTabAsync(observation).ConfigureAwait(false);
                return;
            }

            acceptanceStarted = true;
            acceptanceVersion = attempt.Version;

            using var timeoutCts = new CancellationTokenSource(AcceptanceTimeout);
            var result = await InsertAcceptedSuggestionAsync(attempt, timeoutCts.Token)
                .ConfigureAwait(false);

            if (result.Status == TextReplacementStatus.Success)
            {
                _acceptanceService.CompleteAcceptance(attempt.Version);
                await _overlayService.HideAsync().ConfigureAwait(false);
                return;
            }

            _logger.LogWarning(
                "Prediction Tab acceptance failed with status {Status}.",
                result.Status);

            _acceptanceService.AbortAcceptance(attempt.Version);
            await _overlayService.HideAsync().ConfigureAwait(false);
            await ReinjectTabAsync(observation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (acceptanceStarted)
            {
                _acceptanceService.AbortAcceptance(acceptanceVersion);
            }

            await _overlayService.HideAsync().ConfigureAwait(false);
            await _boundaryKeyDeliveryService
                .DeliverAsync(tabBoundary, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prediction Tab acceptance failed.");

            if (acceptanceStarted)
            {
                _acceptanceService.AbortAcceptance(acceptanceVersion);
            }

            await _overlayService.HideAsync().ConfigureAwait(false);
            await ReinjectTabAsync(observation).ConfigureAwait(false);
        }
        finally
        {
            _acceptanceGate.Release();
        }
    }

    private async Task<TextReplacementResult> InsertAcceptedSuggestionAsync(
        PredictionTabAcceptanceAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(attempt.WordPrefix))
        {
            return await _safeTextReplacementService
                .InsertTextAsync(attempt.SuggestionText, cancellationToken)
                .ConfigureAwait(false);
        }

        var replacementText = attempt.WordPrefix + attempt.SuggestionText;
        return await _safeTextReplacementService
            .ReplaceRecentTextAsync(attempt.WordPrefix, replacementText, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> ValidateAcceptanceContextAsync()
    {
        if (_replacementSessionNotifier.IsReplacementActive)
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

        _lastForegroundWindowHandle = foregroundHandle;

        var caret = await _caretPositionService
            .GetCaretScreenPositionAsync()
            .ConfigureAwait(false);

        if (caret is null
            || caret.WindowHandle == 0
            || caret.CaretHeight <= 0
            || caret.WindowHandle != foregroundHandle)
        {
            return false;
        }

        return true;
    }

    private Task ReinjectTabAsync(KeyboardObservationEventArgs observation)
    {
        var tabBoundary = DeferredBoundaryKey.FromVirtualKey(
            observation.VirtualKeyCode,
            observation.ScanCode);

        return _boundaryKeyDeliveryService.DeliverAsync(tabBoundary);
    }
}
