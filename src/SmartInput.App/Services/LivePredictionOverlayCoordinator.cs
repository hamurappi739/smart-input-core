using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.App.Services;

public interface ILivePredictionOverlayCoordinator
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SyncOverlayAsync();
}

public sealed class LivePredictionOverlayCoordinator : ILivePredictionOverlayCoordinator, IAsyncDisposable
{
    public static readonly TimeSpan SuggestionExpiry =
        TimeSpan.FromSeconds(PredictionOptions.DefaultSuggestionExpirySeconds);
    internal static readonly TimeSpan FocusMonitorInterval = TimeSpan.FromMilliseconds(200);

    private readonly ILivePredictionEngine _predictionEngine;
    private readonly IPredictionOverlayService _overlayService;
    private readonly ICaretPositionService _caretPositionService;
    private readonly IActiveApplicationService _activeApplicationService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ISettingsService _settingsService;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;
    private readonly ILogger<LivePredictionOverlayCoordinator> _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private CancellationTokenSource? _monitorCancellation;
    private nint _lastForegroundWindowHandle;
    private bool _initialized;
    private bool _disposed;

    public LivePredictionOverlayCoordinator(
        ILivePredictionEngine predictionEngine,
        IPredictionOverlayService overlayService,
        ICaretPositionService caretPositionService,
        IActiveApplicationService activeApplicationService,
        IAutomationSafetyService automationSafetyService,
        IEmergencyPauseService emergencyPauseService,
        ISettingsService settingsService,
        IPerformanceMetricsRecorder performanceMetrics,
        ILogger<LivePredictionOverlayCoordinator> logger)
    {
        _predictionEngine = predictionEngine;
        _overlayService = overlayService;
        _caretPositionService = caretPositionService;
        _activeApplicationService = activeApplicationService;
        _automationSafetyService = automationSafetyService;
        _emergencyPauseService = emergencyPauseService;
        _settingsService = settingsService;
        _performanceMetrics = performanceMetrics;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _predictionEngine.OverlayStateChanged += OnOverlayStateChanged;
        _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = MonitorFocusAsync(_monitorCancellation.Token);

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
        _predictionEngine.OverlayStateChanged -= OnOverlayStateChanged;

        if (_monitorCancellation is not null)
        {
            await _monitorCancellation.CancelAsync().ConfigureAwait(false);
            _monitorCancellation.Dispose();
        }

        await _overlayService.ShutdownAsync().ConfigureAwait(false);
        _syncGate.Dispose();
    }

    private void OnOverlayStateChanged()
    {
        _ = SyncOverlayAsync();
    }

    private async Task MonitorFocusAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FocusMonitorInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var activeApplication = await _activeApplicationService
                    .GetActiveApplicationAsync(cancellationToken)
                    .ConfigureAwait(false);

                var foregroundHandle = activeApplication?.WindowHandle ?? 0;
                if (foregroundHandle == _lastForegroundWindowHandle)
                {
                    continue;
                }

                _lastForegroundWindowHandle = foregroundHandle;
                await HideOverlayAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prediction overlay focus monitor failed.");
        }
    }

    public async Task SyncOverlayAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _syncGate.WaitAsync().ConfigureAwait(false);

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            if (!await ShouldShowOverlayAsync().ConfigureAwait(false))
            {
                await HideOverlayAsync().ConfigureAwait(false);
                return;
            }

            if (!_predictionEngine.TryGetOverlaySnapshot(out var snapshot))
            {
                await HideOverlayAsync().ConfigureAwait(false);
                return;
            }

            if (IsSuggestionExpired(snapshot))
            {
                await HideOverlayAsync().ConfigureAwait(false);
                return;
            }

            var caret = await _caretPositionService
                .GetCaretScreenPositionAsync()
                .ConfigureAwait(false);

            if (caret is null || !IsCaretValid(caret))
            {
                await HideOverlayAsync().ConfigureAwait(false);
                return;
            }

            if (caret.WindowHandle != _lastForegroundWindowHandle)
            {
                await HideOverlayAsync().ConfigureAwait(false);
                return;
            }

            var content = new PredictionOverlayContent(snapshot.SuggestionText);
            var placement = new PredictionOverlayPlacement(caret.X, caret.Y, caret.CaretHeight);

            if (_overlayService.Status.Visibility == PredictionOverlayVisibility.Visible)
            {
                await _overlayService.UpdateAsync(content, placement).ConfigureAwait(false);
            }
            else
            {
                await _overlayService.ShowAsync(content, placement).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prediction overlay synchronization failed.");
            await HideOverlayAsync().ConfigureAwait(false);
        }
        finally
        {
            _performanceMetrics.RecordDuration(
                PerformanceMetricKind.OverlayUpdate,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            _syncGate.Release();
        }
    }

    private async Task<bool> ShouldShowOverlayAsync()
    {
        if (_emergencyPauseService.IsPaused)
        {
            return false;
        }

        var settings = _settingsService.Current;
        if (!settings.IsEnabled || !settings.PredictionEnabled)
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

        var status = _predictionEngine.Status;
        return status.HasSuggestion;
    }

    private static bool IsSuggestionExpired(LivePredictionOverlaySnapshot snapshot)
    {
        if (snapshot.UpdatedAt == default)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - snapshot.UpdatedAt > SuggestionExpiry;
    }

    private static bool IsCaretValid(CaretScreenPosition caret)
    {
        return caret.WindowHandle != 0
            && caret.CaretHeight > 0
            && caret.X >= 0
            && caret.Y >= 0;
    }

    private Task HideOverlayAsync()
    {
        if (_overlayService.Status.Visibility == PredictionOverlayVisibility.Hidden)
        {
            return Task.CompletedTask;
        }

        return _overlayService.HideAsync();
    }
}
