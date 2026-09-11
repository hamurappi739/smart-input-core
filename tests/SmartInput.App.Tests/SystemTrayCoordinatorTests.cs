using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.App.Services;
using SmartInput.App.ViewModels;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Abstractions.Tray;

namespace SmartInput.App.Tests;

public class SystemTrayCoordinatorTests
{
    [Fact]
    public async Task InitializeAsync_ShowsTrayOnceAndRefreshesMenu()
    {
        var tray = new RecordingTrayService { ShowResult = true };
        var settings = new TrayFakeSettingsService(new AppSettings());
        var coordinator = CreateCoordinator(tray, settings);

        await coordinator.InitializeAsync();
        await coordinator.InitializeAsync();

        Assert.Equal(1, tray.ShowCallCount);
        Assert.Equal(1, tray.UpdateMenuCallCount);
    }

    [Fact]
    public async Task DisposeAsync_ShutsDownTray()
    {
        var tray = new RecordingTrayService { ShowResult = true };
        var coordinator = CreateCoordinator(tray, new TrayFakeSettingsService(new AppSettings()));

        await coordinator.InitializeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(1, tray.ShutdownCallCount);
    }

    [Fact]
    public async Task InitializeAsync_TrayCreationFailure_DoesNotThrow()
    {
        var tray = new RecordingTrayService { ShowResult = false };
        var coordinator = CreateCoordinator(tray, new TrayFakeSettingsService(new AppSettings()));

        await coordinator.InitializeAsync();

        Assert.Equal(1, tray.ShowCallCount);
        Assert.Equal(0, tray.UpdateMenuCallCount);
        Assert.False(coordinator.IsTrayAvailable);
    }

    [Fact]
    public async Task InitializeAsync_TrayCreationSuccess_ReportsTrayAvailable()
    {
        var coordinator = CreateCoordinator(
            new RecordingTrayService { ShowResult = true },
            new TrayFakeSettingsService(new AppSettings()));

        await coordinator.InitializeAsync();

        Assert.True(coordinator.IsTrayAvailable);
    }

    [Fact]
    public async Task MenuAction_ToggleProtection_UpdatesSettingsAndMonitoring()
    {
        var tray = new RecordingTrayService { ShowResult = true };
        var settings = new TrayFakeSettingsService(new AppSettings { IsEnabled = true });
        var diagnostic = new RecordingDiagnosticCoordinator();
        var coordinator = CreateCoordinator(tray, settings, diagnostic);

        await coordinator.InitializeAsync();
        await coordinator.ProcessMenuActionAsync(SystemTrayMenuAction.ToggleProtection);

        Assert.False(settings.Current.IsEnabled);
        Assert.Equal(1, diagnostic.ApplyProtectionCallCount);
        Assert.False(diagnostic.LastProtectionEnabled);
    }

    [Fact]
    public async Task MenuAction_ToggleAutomaticLayout_UpdatesSettings()
    {
        var settings = new TrayFakeSettingsService(new AppSettings { AutomaticLayoutEnabled = false });
        var coordinator = CreateCoordinator(new RecordingTrayService { ShowResult = true }, settings);

        await coordinator.InitializeAsync();
        await coordinator.ProcessMenuActionAsync(SystemTrayMenuAction.ToggleAutomaticLayout);

        Assert.True(settings.Current.AutomaticLayoutEnabled);
    }

    [Fact]
    public async Task MenuAction_ToggleEmergencyPause_UsesEmergencyPauseService()
    {
        var emergencyPause = new TrayFakeEmergencyPauseService();
        var coordinator = CreateCoordinator(
            new RecordingTrayService { ShowResult = true },
            new TrayFakeSettingsService(new AppSettings()),
            emergencyPause: emergencyPause);

        await coordinator.InitializeAsync();
        await coordinator.ProcessMenuActionAsync(SystemTrayMenuAction.ToggleEmergencyPause);

        Assert.True(emergencyPause.IsPaused);
        Assert.True(emergencyPause.PauseCallCount >= 1);
    }

    [Fact]
    public async Task MenuAction_ToggleEmergencyPause_WhenPaused_Resumes()
    {
        var emergencyPause = new TrayFakeEmergencyPauseService { IsPaused = true };
        var coordinator = CreateCoordinator(
            new RecordingTrayService { ShowResult = true },
            new TrayFakeSettingsService(new AppSettings { EmergencyPauseEnabled = true }),
            emergencyPause: emergencyPause);

        await coordinator.InitializeAsync();
        await coordinator.ProcessMenuActionAsync(SystemTrayMenuAction.ToggleEmergencyPause);

        Assert.False(emergencyPause.IsPaused);
        Assert.Equal(1, emergencyPause.ResumeCallCount);
    }

    [Fact]
    public async Task MenuAction_OpenSettings_ActivatesExistingWindow()
    {
        var shell = new RecordingApplicationShell();
        var startup = new RecordingStartupCoordinator();
        var coordinator = CreateCoordinator(
            new RecordingTrayService { ShowResult = true },
            new TrayFakeSettingsService(new AppSettings()),
            shell: shell,
            startup: startup);

        await coordinator.InitializeAsync();
        await coordinator.ProcessMenuActionAsync(SystemTrayMenuAction.OpenSettings);

        Assert.Equal(1, shell.ShowOrActivateCallCount);
        Assert.Equal(1, startup!.EnsureMainWindowCallCount);
    }

    [Fact]
    public async Task MenuAction_Exit_RequestsApplicationExit()
    {
        var shell = new RecordingApplicationShell();
        var coordinator = CreateCoordinator(
            new RecordingTrayService { ShowResult = true },
            new TrayFakeSettingsService(new AppSettings()),
            shell: shell);

        await coordinator.InitializeAsync();
        await coordinator.ProcessMenuActionAsync(SystemTrayMenuAction.Exit);

        Assert.Equal(1, shell.ExitCallCount);
    }

    [Fact]
    public async Task SettingsChanged_RefreshesTrayMenuState()
    {
        var tray = new RecordingTrayService { ShowResult = true };
        var settings = new TrayFakeSettingsService(new AppSettings { PredictionEnabled = false });
        var coordinator = CreateCoordinator(tray, settings);

        await coordinator.InitializeAsync();
        tray.UpdateMenuCallCount = 0;

        await settings.UpdateAsync(appSettings => appSettings.PredictionEnabled = true);

        await Task.Delay(50);

        Assert.True(tray.UpdateMenuCallCount >= 1);
        Assert.True(tray.LastMenuState?.IsPredictionEnabled);
    }

    [Fact]
    public async Task SettingsChanged_EnablingAutocorrect_ShowsDesktopNotification()
    {
        var tray = new RecordingTrayService { ShowResult = true };
        var settings = new TrayFakeSettingsService(new AppSettings { AutocorrectEnabled = false });
        var coordinator = CreateCoordinator(tray, settings);

        await coordinator.InitializeAsync();
        tray.NotificationCallCount = 0;

        await settings.UpdateAsync(appSettings => appSettings.AutocorrectEnabled = true);
        await Task.Delay(50);

        Assert.Equal(1, tray.NotificationCallCount);
        Assert.Equal("Smart Input", tray.LastNotificationTitle);
        Assert.Equal("Исправление опечаток включено.", tray.LastNotificationMessage);
    }

    [Fact]
    public async Task RefreshTrayState_ReflectsCurrentSettings()
    {
        var tray = new RecordingTrayService { ShowResult = true };
        var settings = new TrayFakeSettingsService(new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = true,
            AutocorrectEnabled = true,
            PredictionEnabled = false,
            SnippetsEnabled = true,
        });

        var coordinator = CreateCoordinator(
            tray,
            settings,
            emergencyPause: new TrayFakeEmergencyPauseService { IsPaused = true });

        await coordinator.RefreshTrayStateAsync();

        Assert.NotNull(tray.LastMenuState);
        Assert.True(tray.LastMenuState!.IsProtectionEnabled);
        Assert.True(tray.LastMenuState.IsAutomaticLayoutEnabled);
        Assert.True(tray.LastMenuState.IsAutocorrectEnabled);
        Assert.False(tray.LastMenuState.IsPredictionEnabled);
        Assert.True(tray.LastMenuState.IsSnippetsEnabled);
        Assert.True(tray.LastMenuState.IsEmergencyPaused);
        Assert.Equal("SmartInput (Экстренная пауза)", tray.LastTooltip);
    }

    [Fact]
    public async Task CorrectionsViewModel_ExternalSettingsChange_SyncsToggleState()
    {
        var settings = new TrayFakeSettingsService(new AppSettings { PredictionEnabled = false });
        var viewModel = new CorrectionsViewModel(settings);

        await Task.Delay(50);
        Assert.False(viewModel.PredictionEnabled);

        await settings.UpdateAsync(appSettings => appSettings.PredictionEnabled = true);
        await Task.Delay(50);

        Assert.True(viewModel.PredictionEnabled);
    }

    private static SystemTrayCoordinator CreateCoordinator(
        RecordingTrayService tray,
        TrayFakeSettingsService settings,
        RecordingDiagnosticCoordinator? diagnostic = null,
        TrayFakeEmergencyPauseService? emergencyPause = null,
        RecordingApplicationShell? shell = null,
        RecordingStartupCoordinator? startup = null)
    {
        return new SystemTrayCoordinator(
            tray,
            settings,
            emergencyPause ?? new TrayFakeEmergencyPauseService(),
            diagnostic ?? new RecordingDiagnosticCoordinator(),
            shell ?? new RecordingApplicationShell(),
            startup ?? new RecordingStartupCoordinator(),
            NullLogger<SystemTrayCoordinator>.Instance);
    }

    private sealed class RecordingTrayService : ISystemTrayPlatformService
    {
        public bool ShowResult { get; init; } = true;

        public int ShowCallCount { get; private set; }

        public int UpdateMenuCallCount { get; set; }

        public int ShutdownCallCount { get; private set; }

        public SystemTrayMenuState? LastMenuState { get; private set; }

        public string? LastTooltip { get; private set; }

        public int NotificationCallCount { get; set; }

        public string? LastNotificationTitle { get; private set; }

        public string? LastNotificationMessage { get; private set; }

        public event Action<SystemTrayMenuAction>? MenuActionRequested;

        public Task<bool> TryShowAsync(CancellationToken cancellationToken = default)
        {
            ShowCallCount++;
            return Task.FromResult(ShowResult);
        }

        public Task HideAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateMenuStateAsync(SystemTrayMenuState state, CancellationToken cancellationToken = default)
        {
            UpdateMenuCallCount++;
            LastMenuState = state;
            return Task.CompletedTask;
        }

        public Task SetTooltipAsync(string tooltip, CancellationToken cancellationToken = default)
        {
            LastTooltip = tooltip;
            return Task.CompletedTask;
        }

        public Task ShowNotificationAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default)
        {
            NotificationCallCount++;
            LastNotificationTitle = title;
            LastNotificationMessage = message;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownCallCount++;
            return Task.CompletedTask;
        }

        public void RaiseMenuAction(SystemTrayMenuAction action)
        {
            MenuActionRequested?.Invoke(action);
        }
    }

    private sealed class TrayFakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class TrayFakeEmergencyPauseService : IEmergencyPauseService
    {
        public bool IsPaused { get; set; }

        public int PauseCallCount { get; private set; }

        public int ResumeCallCount { get; private set; }

        public void Pause()
        {
            PauseCallCount++;
            IsPaused = true;
        }

        public void Resume()
        {
            ResumeCallCount++;
            IsPaused = false;
        }
    }

    private sealed class RecordingDiagnosticCoordinator : IInputDiagnosticCoordinator
    {
        public int ApplyProtectionCallCount { get; private set; }

        public bool LastProtectionEnabled { get; private set; }

        public bool IsMonitoring => false;

        public event EventHandler<InputDiagnosticEvent>? DiagnosticReceived;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyProtectionStateAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            ApplyProtectionCallCount++;
            LastProtectionEnabled = enabled;
            return Task.CompletedTask;
        }

        public IReadOnlyList<InputDiagnosticEvent> GetRecentEvents() => [];

        public void ClearRecentEvents()
        {
        }
    }

    private sealed class RecordingApplicationShell : IApplicationShell
    {
        public int ShowOrActivateCallCount { get; private set; }

        public int ExitCallCount { get; private set; }

        public bool IsCloseToTrayEnabled { get; private set; }

        public void Attach(
            IApplicationShutdownHost shutdownHost,
            ViewModels.MainWindowViewModel mainWindowViewModel,
            IMainWindowPresenter mainWindow)
        {
        }

        public void AttachShutdownHost(IApplicationShutdownHost shutdownHost)
        {
        }

        public void ConfigureCloseToTray(bool enabled)
        {
            IsCloseToTrayEnabled = enabled;
        }

        public void ShowOrActivateMainWindow()
        {
            ShowOrActivateCallCount++;
        }

        public void ExitApplication()
        {
            ExitCallCount++;
        }
    }

    private sealed class RecordingStartupCoordinator : IApplicationStartupCoordinator
    {
        public bool IsMainWindowPrepared => true;

        public bool IsTrayOnlyMode => false;

        public bool AreCoordinatorsInitialized => true;

        public Avalonia.Controls.Window? MainWindow => null;

        public int EnsureMainWindowCallCount { get; private set; }

        public void PrepareMainWindow(IDesktopApplicationHost desktop)
        {
        }

        public void PrepareTrayOnly(IDesktopApplicationHost desktop)
        {
        }

        public void EnsureMainWindow()
        {
            EnsureMainWindowCallCount++;
        }

        public Task InitializeCoordinatorsAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
