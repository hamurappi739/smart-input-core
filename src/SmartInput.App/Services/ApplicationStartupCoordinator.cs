using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Engines;
using SmartInput.Core.Services;
using SmartInput.App.ViewModels;
using SmartInput.App.Views;

namespace SmartInput.App.Services;

public interface IApplicationStartupCoordinator
{
    bool IsMainWindowPrepared { get; }

    bool IsTrayOnlyMode { get; }

    bool AreCoordinatorsInitialized { get; }

    Window? MainWindow { get; }

    void PrepareMainWindow(IDesktopApplicationHost desktop);

    /// <summary>Registers the desktop lifetime without creating the Avalonia settings window.</summary>
    void PrepareTrayOnly(IDesktopApplicationHost desktop);

    /// <summary>Creates the settings window on demand. It is idempotent and does not show it.</summary>
    void EnsureMainWindow();

    Task InitializeCoordinatorsAsync(CancellationToken cancellationToken = default);
}

internal interface IMainWindowBootstrapper
{
    MainWindowBootstrapResult Create();
}

internal sealed class MainWindowBootstrapResult
{
    public required Window Window { get; init; }

    public required MainWindowViewModel ViewModel { get; init; }

    public required IMainWindowPresenter Presenter { get; init; }
}

internal sealed class AvaloniaMainWindowBootstrapper : IMainWindowBootstrapper
{
    private readonly MainWindowViewModel _mainWindowViewModel;

    public AvaloniaMainWindowBootstrapper(MainWindowViewModel mainWindowViewModel)
    {
        _mainWindowViewModel = mainWindowViewModel;
    }

    public MainWindowBootstrapResult Create()
    {
        // Tray-first starts without loading Fluent/Avalonia resources. The
        // resource dictionary is required only when the settings window is
        // actually requested.
        App.EnsureUiResourcesLoaded();

        var window = new MainWindow
        {
            DataContext = _mainWindowViewModel,
        };

        return new MainWindowBootstrapResult
        {
            Window = window,
            ViewModel = _mainWindowViewModel,
            Presenter = new AvaloniaMainWindowPresenter(window),
        };
    }
}

public sealed class ApplicationStartupCoordinator : IApplicationStartupCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationShell _applicationShell;
    private readonly ILogger<ApplicationStartupCoordinator> _logger;
    private readonly bool _trayOnlyMode;
    private readonly object _windowGate = new();

    private IDesktopApplicationHost? _desktopHost;

    private int _mainWindowPrepared;
    private int _coordinatorsInitialized;

    public ApplicationStartupCoordinator(
        IServiceProvider serviceProvider,
        IApplicationShell applicationShell,
        ILogger<ApplicationStartupCoordinator> logger,
        bool? trayOnlyMode = null)
    {
        _serviceProvider = serviceProvider;
        _applicationShell = applicationShell;
        _logger = logger;
        _trayOnlyMode = trayOnlyMode ??
            string.Equals(
                Environment.GetEnvironmentVariable("SMARTINPUT_TRAY_ONLY"),
                "1",
                StringComparison.Ordinal);
    }

    public bool IsMainWindowPrepared => Volatile.Read(ref _mainWindowPrepared) == 1;

    public bool IsTrayOnlyMode => _trayOnlyMode;

    public bool AreCoordinatorsInitialized => Volatile.Read(ref _coordinatorsInitialized) == 1;

    public Window? MainWindow { get; private set; }

    public void PrepareMainWindow(IDesktopApplicationHost desktop)
    {
        PrepareDesktopHost(desktop);
        EnsureMainWindow();
        _applicationShell.ShowOrActivateMainWindow();
    }

    public void PrepareTrayOnly(IDesktopApplicationHost desktop)
    {
        PrepareDesktopHost(desktop);
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    public void EnsureMainWindow()
    {
        if (IsMainWindowPrepared)
        {
            return;
        }

        var desktop = _desktopHost
            ?? throw new InvalidOperationException("Desktop host must be prepared before creating the main window.");

        lock (_windowGate)
        {
            if (IsMainWindowPrepared)
            {
                return;
            }

            var bootstrapper = _serviceProvider.GetRequiredService<IMainWindowBootstrapper>();
            var bootstrap = bootstrapper.Create();

            MainWindow = bootstrap.Window;
            desktop.MainWindow = bootstrap.Window;

            _applicationShell.Attach(
                desktop,
                bootstrap.ViewModel,
                bootstrap.Presenter);

            Volatile.Write(ref _mainWindowPrepared, 1);
        }
    }

    private void PrepareDesktopHost(IDesktopApplicationHost desktop)
    {
        _desktopHost ??= desktop;
        _applicationShell.AttachShutdownHost(desktop);
    }

    public async Task InitializeCoordinatorsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsMainWindowPrepared && !IsTrayOnlyMode)
        {
            throw new InvalidOperationException("Main window must be prepared before coordinator initialization.");
        }

        if (_desktopHost is null)
        {
            throw new InvalidOperationException("Desktop host must be prepared before coordinator initialization.");
        }

        if (Interlocked.CompareExchange(ref _coordinatorsInitialized, 1, 0) != 0)
        {
            return;
        }

        try
        {
            // In the normal windowed profile, load bundled dictionaries before
            // the keyboard hook is active. Tray-first deliberately defers this
            // optional, memory-heavy warm-up until a correction query needs it;
            // the first query remains safe because the provider is lazy and
            // bounded. This keeps the resident idle profile as small as possible.
            if (!IsTrayOnlyMode)
            {
                _serviceProvider.GetService<IHunspellWordFormProvider>()?.WarmUp();
                if (_serviceProvider.GetService<ISettingsService>()?.Current.ExternalSpellingEngineEnabled == true)
                {
                    _serviceProvider.GetService<SymSpellSpellCorrectionProvider>()?.WarmUp();
                }
            }

            await _serviceProvider.GetRequiredService<IInputDiagnosticCoordinator>()
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);

            await _serviceProvider.GetRequiredService<ILiveLayoutCorrectionCoordinator>()
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);

            await _serviceProvider.GetRequiredService<ICorrectionNotificationCoordinator>()
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);

            await _serviceProvider.GetRequiredService<IAutomaticLayoutHotkeyCoordinator>()
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);

            await _serviceProvider.GetRequiredService<IDoubleShiftUndoCoordinator>()
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(true);

            var systemTrayCoordinator = _serviceProvider.GetRequiredService<ISystemTrayCoordinator>();
            await systemTrayCoordinator.InitializeAsync(cancellationToken).ConfigureAwait(true);

            // A tray-less environment must remain usable. Fail open to the
            // settings window instead of leaving a headless process running.
            if (IsTrayOnlyMode && !systemTrayCoordinator.IsTrayAvailable)
            {
                EnsureMainWindow();
                _applicationShell.ShowOrActivateMainWindow();
            }

            _applicationShell.ConfigureCloseToTray(systemTrayCoordinator.IsTrayAvailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Exchange(ref _coordinatorsInitialized, 0);
            _logger.LogError(ex, "Background coordinator initialization failed.");
            if (IsTrayOnlyMode && !IsMainWindowPrepared)
            {
                try
                {
                    EnsureMainWindow();
                    _applicationShell.ShowOrActivateMainWindow();
                }
                catch (Exception fallbackException)
                {
                    _logger.LogError(fallbackException, "Failed to create fallback settings window.");
                }
            }
            _applicationShell.ConfigureCloseToTray(false);
            throw;
        }
    }
}
