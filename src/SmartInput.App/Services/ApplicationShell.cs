using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SmartInput.App.ViewModels;

namespace SmartInput.App.Services;

public interface IApplicationShell
{
    bool IsCloseToTrayEnabled { get; }

    void Attach(
        IApplicationShutdownHost shutdownHost,
        MainWindowViewModel mainWindowViewModel,
        IMainWindowPresenter mainWindow);

    /// <summary>Attaches the lifetime host before the settings window exists (tray-first mode).</summary>
    void AttachShutdownHost(IApplicationShutdownHost shutdownHost);

    void ConfigureCloseToTray(bool enabled);

    void ShowOrActivateMainWindow();

    void ExitApplication();
}

public sealed class ApplicationShell : IApplicationShell
{
    private readonly MainWindowLifecyclePolicy _lifecyclePolicy = new();

    private IApplicationShutdownHost? _shutdownHost;
    private MainWindowViewModel? _mainWindowViewModel;
    private IMainWindowPresenter? _mainWindow;

    public bool IsCloseToTrayEnabled => _lifecyclePolicy.IsCloseToTrayEnabled;

    public void Attach(
        IApplicationShutdownHost shutdownHost,
        MainWindowViewModel mainWindowViewModel,
        IMainWindowPresenter mainWindow)
    {
        _shutdownHost = shutdownHost;
        _mainWindowViewModel = mainWindowViewModel;
        _mainWindow = mainWindow;
        _mainWindow.Closing += OnMainWindowClosing;
    }

    public void AttachShutdownHost(IApplicationShutdownHost shutdownHost)
    {
        _shutdownHost = shutdownHost;
    }

    public void ConfigureCloseToTray(bool enabled)
    {
        _lifecyclePolicy.IsCloseToTrayEnabled = enabled;

        if (enabled && _shutdownHost is not null)
        {
            _shutdownHost.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    public void ShowOrActivateMainWindow()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowOrActivateMainWindowCore();
            return;
        }

        Dispatcher.UIThread.Post(ShowOrActivateMainWindowCore);
    }

    public void ExitApplication()
    {
        _lifecyclePolicy.RequestExplicitShutdown();

        var shutdownHost = _shutdownHost;
        if (shutdownHost is null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            shutdownHost.Shutdown();
            return;
        }

        Dispatcher.UIThread.Post(() => shutdownHost.Shutdown());
    }

    internal void ShowOrActivateMainWindowCore()
    {
        var mainWindow = _mainWindow;
        if (mainWindow is null)
        {
            return;
        }

        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();

        if (_mainWindowViewModel?.NavigationItems.Count > 0)
        {
            _mainWindowViewModel.SelectedNavigationItem = _mainWindowViewModel.NavigationItems[0];
        }
    }

    internal MainWindowCloseOutcome EvaluateCloseRequest()
    {
        return _lifecyclePolicy.EvaluateCloseRequest();
    }

    internal void HandleMainWindowClosing(MainWindowClosingEventArgs e)
    {
        if (_lifecyclePolicy.EvaluateCloseRequest() != MainWindowCloseOutcome.CancelAndHide)
        {
            return;
        }

        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private void OnMainWindowClosing(object? sender, MainWindowClosingEventArgs e)
    {
        HandleMainWindowClosing(e);
    }
}
