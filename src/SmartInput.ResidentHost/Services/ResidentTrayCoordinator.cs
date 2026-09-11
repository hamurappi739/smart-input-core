using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartInput.App.Services;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Abstractions.Tray;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Native tray menu for the resident runtime. Unlike the Avalonia coordinator,
/// it never creates a window in this process. "Settings" launches a short
/// lived UI-only SmartInput process against the same local settings file.
/// </summary>
internal sealed class ResidentTrayCoordinator : IAsyncDisposable
{
    private readonly ISystemTrayPlatformService _tray;
    private readonly ISettingsService _settings;
    private readonly IEmergencyPauseService _emergencyPause;
    private readonly IInputDiagnosticCoordinator _diagnostics;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ResidentTrayCoordinator> _logger;

    private bool _initialized;
    private bool _disposed;
    private bool _lastProtectionEnabled;
    private bool _lastAutomaticLayoutEnabled;
    private bool _lastAutocorrectEnabled;

    public ResidentTrayCoordinator(
        ISystemTrayPlatformService tray,
        ISettingsService settings,
        IEmergencyPauseService emergencyPause,
        IInputDiagnosticCoordinator diagnostics,
        IHostApplicationLifetime lifetime,
        ILogger<ResidentTrayCoordinator> logger)
    {
        _tray = tray;
        _settings = settings;
        _emergencyPause = emergencyPause;
        _diagnostics = diagnostics;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        _tray.MenuActionRequested += OnMenuActionRequested;
        _settings.SettingsChanged += OnSettingsChanged;

        _lastProtectionEnabled = _settings.Current.IsEnabled;
        _lastAutomaticLayoutEnabled = _settings.Current.AutomaticLayoutEnabled;
        _lastAutocorrectEnabled = _settings.Current.AutocorrectEnabled;

        if (!await _tray.TryShowAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogError("Native tray icon could not be created; stopping resident runtime to avoid an invisible hook process.");
            _lifetime.StopApplication();
            return;
        }

        _initialized = true;
        await RefreshAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tray.MenuActionRequested -= OnMenuActionRequested;
        _settings.SettingsChanged -= OnSettingsChanged;
        await _tray.ShutdownAsync().ConfigureAwait(false);
    }

    private void OnSettingsChanged()
    {
        var settings = _settings.Current;
        var notification = GetEnablementNotification(settings);
        _lastProtectionEnabled = settings.IsEnabled;
        _lastAutomaticLayoutEnabled = settings.AutomaticLayoutEnabled;
        _lastAutocorrectEnabled = settings.AutocorrectEnabled;

        _ = RefreshAsync();
        if (notification is not null)
        {
            _ = _tray.ShowNotificationAsync(notification.Value.Title, notification.Value.Message);
        }
    }

    private void OnMenuActionRequested(SystemTrayMenuAction action) => _ = ProcessAsync(action);

    private async Task ProcessAsync(SystemTrayMenuAction action)
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
                    await _settings.UpdateAsync(x => x.IsEnabled = !x.IsEnabled).ConfigureAwait(false);
                    await _diagnostics.ApplyProtectionStateAsync(_settings.Current.IsEnabled).ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleAutomaticLayout:
                    await ToggleAsync(x => x.AutomaticLayoutEnabled = !x.AutomaticLayoutEnabled).ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleAutocorrect:
                    await ToggleAsync(x => x.AutocorrectEnabled = !x.AutocorrectEnabled).ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.TogglePrediction:
                    await ToggleAsync(x => x.PredictionEnabled = !x.PredictionEnabled).ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleSnippets:
                    await ToggleAsync(x => x.SnippetsEnabled = !x.SnippetsEnabled).ConfigureAwait(false);
                    break;
                case SystemTrayMenuAction.ToggleEmergencyPause:
                    if (_emergencyPause.IsPaused)
                    {
                        _emergencyPause.Resume();
                    }
                    else
                    {
                        _emergencyPause.Pause();
                    }
                    break;
                case SystemTrayMenuAction.OpenSettings:
                    OpenSettingsWindow();
                    break;
                case SystemTrayMenuAction.Exit:
                    _lifetime.StopApplication();
                    return;
            }

            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Resident tray action failed safely: {Action}.", action);
        }
    }

    private Task ToggleAsync(Action<AppSettings> update) => _settings.UpdateAsync(update);

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

    private async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        var settings = _settings.Current;
        await _tray.UpdateMenuStateAsync(new SystemTrayMenuState
        {
            IsProtectionEnabled = settings.IsEnabled,
            IsAutomaticLayoutEnabled = settings.AutomaticLayoutEnabled,
            IsAutocorrectEnabled = settings.AutocorrectEnabled,
            IsPredictionEnabled = settings.PredictionEnabled,
            IsSnippetsEnabled = settings.SnippetsEnabled,
            IsEmergencyPaused = _emergencyPause.IsPaused,
        }).ConfigureAwait(false);

        await _tray.SetTooltipAsync(
            _emergencyPause.IsPaused ? "SmartInput (Экстренная пауза)" : "SmartInput")
            .ConfigureAwait(false);
    }

    private void OpenSettingsWindow()
    {
        var configured = Environment.GetEnvironmentVariable("SMARTINPUT_UI_EXE");
        var executable = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "SmartInput.exe")
            : configured;

        if (!File.Exists(executable))
        {
            throw new InvalidOperationException("The SmartInput settings executable was not found beside the resident host.");
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
        };
        startInfo.Environment["SMARTINPUT_UI_ONLY"] = "1";
        Process.Start(startInfo);
    }
}
