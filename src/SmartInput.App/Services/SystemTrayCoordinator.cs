using Microsoft.Extensions.Logging;
using Avalonia.Threading;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Abstractions.Tray;

namespace SmartInput.App.Services;

public interface ISystemTrayCoordinator
{
    bool IsTrayAvailable { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemTrayCoordinator : ISystemTrayCoordinator, IAsyncDisposable
{
    private readonly ISystemTrayPlatformService _trayService;
    private readonly ISettingsService _settingsService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly IInputDiagnosticCoordinator _diagnosticCoordinator;
    private readonly IApplicationShell _applicationShell;
    private readonly IApplicationStartupCoordinator _startupCoordinator;
    private readonly ILogger<SystemTrayCoordinator> _logger;

    private bool _initialized;
    private bool _disposed;
    private bool _lastProtectionEnabled;
    private bool _lastAutomaticLayoutEnabled;
    private bool _lastAutocorrectEnabled;

    public bool IsTrayAvailable { get; private set; }

    public SystemTrayCoordinator(
        ISystemTrayPlatformService trayService,
        ISettingsService settingsService,
        IEmergencyPauseService emergencyPauseService,
        IInputDiagnosticCoordinator diagnosticCoordinator,
        IApplicationShell applicationShell,
        IApplicationStartupCoordinator startupCoordinator,
        ILogger<SystemTrayCoordinator> logger)
    {
        _trayService = trayService;
        _settingsService = settingsService;
        _emergencyPauseService = emergencyPauseService;
        _diagnosticCoordinator = diagnosticCoordinator;
        _applicationShell = applicationShell;
        _startupCoordinator = startupCoordinator;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _trayService.MenuActionRequested += OnMenuActionRequested;
        _settingsService.SettingsChanged += OnSettingsChanged;

        _lastProtectionEnabled = _settingsService.Current.IsEnabled;
        _lastAutomaticLayoutEnabled = _settingsService.Current.AutomaticLayoutEnabled;
        _lastAutocorrectEnabled = _settingsService.Current.AutocorrectEnabled;

        var shown = await _trayService.TryShowAsync(cancellationToken).ConfigureAwait(false);
        IsTrayAvailable = shown;
        if (!shown)
        {
            _logger.LogWarning("System tray icon could not be created; continuing without tray.");
        }
        else
        {
            await RefreshTrayStateAsync().ConfigureAwait(false);
        }

        _initialized = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayService.MenuActionRequested -= OnMenuActionRequested;
        _settingsService.SettingsChanged -= OnSettingsChanged;

        try
        {
            await _trayService.ShutdownAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System tray shutdown failed.");
        }
    }

    private void OnSettingsChanged()
    {
        var settings = _settingsService.Current;
        var notification = GetEnablementNotification(settings);
        _lastProtectionEnabled = settings.IsEnabled;
        _lastAutomaticLayoutEnabled = settings.AutomaticLayoutEnabled;
        _lastAutocorrectEnabled = settings.AutocorrectEnabled;

        _ = RefreshTrayStateAsync();
        if (notification is not null)
        {
            _ = _trayService.ShowNotificationAsync(notification.Value.Title, notification.Value.Message);
        }
    }

    private void OnMenuActionRequested(SystemTrayMenuAction action)
    {
        _ = ProcessMenuActionAsync(action);
    }

    internal async Task ProcessMenuActionAsync(SystemTrayMenuAction action)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            switch (action)
            {
                case SystemTrayMenuAction.ToggleProtection:
                    await ToggleSettingAsync(settings => settings.IsEnabled = !settings.IsEnabled)
                        .ConfigureAwait(false);
                    await _diagnosticCoordinator
                        .ApplyProtectionStateAsync(_settingsService.Current.IsEnabled)
                        .ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleAutomaticLayout:
                    await ToggleSettingAsync(settings =>
                            settings.AutomaticLayoutEnabled = !settings.AutomaticLayoutEnabled)
                        .ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleAutocorrect:
                    await ToggleSettingAsync(settings =>
                            settings.AutocorrectEnabled = !settings.AutocorrectEnabled)
                        .ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.TogglePrediction:
                    await ToggleSettingAsync(settings =>
                            settings.PredictionEnabled = !settings.PredictionEnabled)
                        .ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleSnippets:
                    await ToggleSettingAsync(settings =>
                            settings.SnippetsEnabled = !settings.SnippetsEnabled)
                        .ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleEmergencyPause:
                    if (_emergencyPauseService.IsPaused)
                    {
                        _emergencyPauseService.Resume();
                    }
                    else
                    {
                        _emergencyPauseService.Pause();
                    }

                    await RefreshTrayStateAsync().ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.OpenSettings:
                    if (_startupCoordinator.IsTrayOnlyMode && !Dispatcher.UIThread.CheckAccess())
                    {
                        await Dispatcher.UIThread
                            .InvokeAsync(_startupCoordinator.EnsureMainWindow);
                    }
                    else
                    {
                        _startupCoordinator.EnsureMainWindow();
                    }
                    _applicationShell.ShowOrActivateMainWindow();
                    break;
                case SystemTrayMenuAction.Exit:
                    _applicationShell.ExitApplication();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process tray menu action {Action}.", action);
        }
    }

    internal async Task RefreshTrayStateAsync()
    {
        var settings = _settingsService.Current;
        await _trayService.UpdateMenuStateAsync(new SystemTrayMenuState
        {
            IsProtectionEnabled = settings.IsEnabled,
            IsAutomaticLayoutEnabled = settings.AutomaticLayoutEnabled,
            IsAutocorrectEnabled = settings.AutocorrectEnabled,
            IsPredictionEnabled = settings.PredictionEnabled,
            IsSnippetsEnabled = settings.SnippetsEnabled,
            IsEmergencyPaused = _emergencyPauseService.IsPaused,
        }).ConfigureAwait(false);

        var tooltip = _emergencyPauseService.IsPaused
            ? "SmartInput (Экстренная пауза)"
            : "SmartInput";

        await _trayService.SetTooltipAsync(tooltip).ConfigureAwait(false);
    }

    private Task ToggleSettingAsync(Action<AppSettings> toggle)
    {
        return _settingsService.UpdateAsync(toggle);
    }

    private (string Title, string Message)? GetEnablementNotification(AppSettings settings)
    {
        if (settings.IsEnabled && !_lastProtectionEnabled)
        {
            return ("Smart Input", "Автоисправление включено.");
        }

        if (settings.AutomaticLayoutEnabled && !_lastAutomaticLayoutEnabled)
        {
            return ("Smart Input", "Исправление раскладки включено.");
        }

        if (settings.AutocorrectEnabled && !_lastAutocorrectEnabled)
        {
            return ("Smart Input", "Исправление опечаток включено.");
        }

        return null;
    }
}
