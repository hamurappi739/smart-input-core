using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.App.Services;

public interface ICorrectionNotificationCoordinator
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class CorrectionNotificationCoordinator
    : ICorrectionFeedbackNotifier, ICorrectionNotificationCoordinator, IAsyncDisposable
{
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan FocusMonitorInterval = TimeSpan.FromMilliseconds(200);

    private readonly ICorrectionNotificationOverlayService _overlayService;
    private readonly ICaretPositionService _caretPositionService;
    private readonly IActiveApplicationService _activeApplicationService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<CorrectionNotificationCoordinator> _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private CancellationTokenSource? _autoHideCancellation;
    private CancellationTokenSource? _monitorCancellation;
    private nint _lastForegroundWindowHandle;
    private bool _initialized;
    private bool _disposed;

    public CorrectionNotificationCoordinator(
        ICorrectionNotificationOverlayService overlayService,
        ICaretPositionService caretPositionService,
        IActiveApplicationService activeApplicationService,
        IAutomationSafetyService automationSafetyService,
        IEmergencyPauseService emergencyPauseService,
        ISettingsService settingsService,
        ILogger<CorrectionNotificationCoordinator> logger)
    {
        _overlayService = overlayService;
        _caretPositionService = caretPositionService;
        _activeApplicationService = activeApplicationService;
        _automationSafetyService = automationSafetyService;
        _emergencyPauseService = emergencyPauseService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = MonitorFocusAsync(_monitorCancellation.Token);

        var activeApplication = await _activeApplicationService
            .GetActiveApplicationAsync(cancellationToken)
            .ConfigureAwait(false);
        _lastForegroundWindowHandle = activeApplication?.WindowHandle ?? 0;
        _initialized = true;
    }

    public void NotifySuccessfulCorrection(CorrectionKind kind)
    {
        _ = ShowNotificationAsync(kind);
    }

    public void NotifyCorrectionUndone()
    {
        _ = HideAsync();
    }

    public void NotifyUserInput()
    {
        _ = HideAsync();
    }

    public void NotifyContextInvalidated()
    {
        _ = HideAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_monitorCancellation is not null)
        {
            await _monitorCancellation.CancelAsync().ConfigureAwait(false);
            _monitorCancellation.Dispose();
        }

        CancelAutoHideTimer();
        await _overlayService.ShutdownAsync().ConfigureAwait(false);
        _syncGate.Dispose();
    }

    private async Task ShowNotificationAsync(CorrectionKind kind)
    {
        if (_disposed)
        {
            return;
        }

        await _syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancelAutoHideTimer();

            if (!await ShouldShowNotificationAsync().ConfigureAwait(false))
            {
                await HideOverlayIfVisibleAsync().ConfigureAwait(false);
                return;
            }

            var caret = await _caretPositionService
                .GetCaretScreenPositionAsync()
                .ConfigureAwait(false);

            if (caret is null || !IsCaretValid(caret))
            {
                return;
            }

            var content = new CorrectionNotificationContent(CorrectionNotificationMessages.ForKind(kind));
            var placement = new PredictionOverlayPlacement(
                caret.X,
                caret.Y + caret.CaretHeight,
                caret.CaretHeight);

            await _overlayService.ShowAsync(content, placement).ConfigureAwait(false);
            _lastForegroundWindowHandle = caret.WindowHandle;

            _autoHideCancellation = new CancellationTokenSource();
            var token = _autoHideCancellation.Token;
            _ = AutoHideAfterDelayAsync(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Correction notification display failed.");
            await HideOverlayIfVisibleAsync().ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task AutoHideAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DisplayDuration, cancellationToken).ConfigureAwait(false);
            await HideAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HideAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _syncGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancelAutoHideTimer();
            await HideOverlayIfVisibleAsync().ConfigureAwait(false);
        }
        finally
        {
            _syncGate.Release();
        }
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
                await HideAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Correction notification focus monitor failed.");
        }
    }

    private async Task<bool> ShouldShowNotificationAsync()
    {
        if (_emergencyPauseService.IsPaused)
        {
            return false;
        }

        if (!_settingsService.Current.IsEnabled)
        {
            return false;
        }

        var policy = await _automationSafetyService
            .EvaluateCurrentContextAsync()
            .ConfigureAwait(false);

        return policy.State == AutomationPolicyState.Allowed
            && policy.AllowsAutomation
            && !policy.IsEmergencyPaused;
    }

    private Task HideOverlayIfVisibleAsync()
    {
        if (_overlayService.Status.Visibility == CorrectionNotificationVisibility.Hidden)
        {
            return Task.CompletedTask;
        }

        return _overlayService.HideAsync();
    }

    private void CancelAutoHideTimer()
    {
        if (_autoHideCancellation is null)
        {
            return;
        }

        _autoHideCancellation.Cancel();
        _autoHideCancellation.Dispose();
        _autoHideCancellation = null;
    }

    private static bool IsCaretValid(CaretScreenPosition caret)
    {
        return caret.WindowHandle != 0
            && caret.CaretHeight > 0
            && caret.X >= 0
            && caret.Y >= 0;
    }
}
