using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartInput.App.Services;
using SmartInput.Core.Services;

namespace SmartInput.App;

public partial class App : Application
{
    private static int _uiResourcesLoaded;
    private static App? _instance;

    public App()
    {
        _instance = this;
    }

    public override void Initialize()
    {
        if (!IsTrayOnlyEnvironment() || IsUiOnlyEnvironment())
        {
            LoadUiResourcesIfNeeded();
        }
    }

    internal static void EnsureUiResourcesLoaded()
    {
        if (Volatile.Read(ref _uiResourcesLoaded) != 0)
        {
            return;
        }

        var app = _instance ?? (App)Current!;
        app.LoadUiResourcesIfNeeded();
        Volatile.Write(ref _uiResourcesLoaded, 1);
    }

    private void LoadUiResourcesIfNeeded()
    {
        if (Volatile.Read(ref _uiResourcesLoaded) != 0)
        {
            return;
        }

        AvaloniaXamlLoader.Load(this);
        Volatile.Write(ref _uiResourcesLoaded, 1);
    }

    private static bool IsTrayOnlyEnvironment()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SMARTINPUT_TRAY_ONLY"),
            "1",
            StringComparison.Ordinal);
    }

    private static bool IsUiOnlyEnvironment()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("SMARTINPUT_UI_ONLY"),
            "1",
            StringComparison.Ordinal);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Exit += OnDesktopExit;

            var desktop = new DesktopApplicationHost(desktopLifetime);
            var startup = AppServices.Provider.GetRequiredService<IApplicationStartupCoordinator>();

            if (IsUiOnlyEnvironment())
            {
                // The resident host owns hooks, tray and correction. This
                // lightweight settings process shares only the local config
                // file and must never create a second global keyboard hook.
                AppServices.Provider.GetRequiredService<ISettingsService>()
                    .LoadAsync()
                    .GetAwaiter()
                    .GetResult();
                startup.PrepareMainWindow(desktop);
            }
            else if (startup.IsTrayOnlyMode)
            {
                startup.PrepareTrayOnly(desktop);
            }
            else
            {
                startup.PrepareMainWindow(desktop);
            }

            if (!IsUiOnlyEnvironment())
            {
                _ = InitializeCoordinatorsInBackgroundAsync(startup);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeCoordinatorsInBackgroundAsync(IApplicationStartupCoordinator startup)
    {
        try
        {
            await startup.InitializeCoordinatorsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            var logger = AppServices.Provider.GetService<ILogger<App>>();
            logger?.LogError(ex, "Smart Input background initialization failed; main window remains available.");
        }
    }

    private static void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            var shutdownCoordinator = AppServices.Provider.GetRequiredService<IApplicationShutdownCoordinator>();
            shutdownCoordinator.PerformCleanupAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Keep shutdown resilient; coordinator cleanup must not block exit.
        }
    }
}
