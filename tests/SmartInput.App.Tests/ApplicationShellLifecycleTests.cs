using Avalonia.Controls;
using Avalonia.Threading;
using SmartInput.App.Services;

namespace SmartInput.App.Tests;

[Collection(AppTestCollection.Name)]
public class MainWindowLifecyclePolicyTests
{
    [Fact]
    public void EvaluateCloseRequest_WhenTrayEnabledAndNotExiting_CancelsAndHides()
    {
        var policy = new MainWindowLifecyclePolicy
        {
            IsCloseToTrayEnabled = true,
        };

        Assert.Equal(MainWindowCloseOutcome.CancelAndHide, policy.EvaluateCloseRequest());
    }

    [Fact]
    public void EvaluateCloseRequest_WhenTrayUnavailable_AllowsClose()
    {
        var policy = new MainWindowLifecyclePolicy
        {
            IsCloseToTrayEnabled = false,
        };

        Assert.Equal(MainWindowCloseOutcome.ProceedWithClose, policy.EvaluateCloseRequest());
    }

    [Fact]
    public void EvaluateCloseRequest_WhenExplicitShutdownRequested_AllowsClose()
    {
        var policy = new MainWindowLifecyclePolicy
        {
            IsCloseToTrayEnabled = true,
        };

        policy.RequestExplicitShutdown();

        Assert.Equal(MainWindowCloseOutcome.ProceedWithClose, policy.EvaluateCloseRequest());
    }
}

[Collection(AppTestCollection.Name)]
public class ApplicationShellLifecycleTests
{
    [Fact]
    public void HandleMainWindowClosing_WhenTrayEnabled_CancelsAndHidesSameWindow()
    {
        var shell = CreateShell(out var presenter, closeToTrayEnabled: true);

        var args = new MainWindowClosingEventArgs();
        shell.HandleMainWindowClosing(args);

        Assert.True(args.Cancel);
        Assert.Equal(1, presenter.HideCallCount);
        Assert.False(presenter.IsVisible);
    }

    [Fact]
    public void HandleMainWindowClosing_WhenTrayUnavailable_AllowsClose()
    {
        var shell = CreateShell(out var presenter, closeToTrayEnabled: false);

        var args = new MainWindowClosingEventArgs();
        shell.HandleMainWindowClosing(args);

        Assert.False(args.Cancel);
        Assert.Equal(0, presenter.HideCallCount);
    }

    [Fact]
    public void ShowOrActivateMainWindow_RestoresSameHiddenWindow()
    {
        var shell = CreateShell(out var presenter, closeToTrayEnabled: true);
        presenter.IsVisible = false;

        shell.ShowOrActivateMainWindowCore();

        Assert.Equal(1, presenter.ShowCallCount);
        Assert.Equal(1, presenter.ActivateCallCount);
        Assert.True(presenter.IsVisible);
        Assert.Equal(WindowState.Normal, presenter.WindowState);
    }

    [Fact]
    public void RepeatedCloseAndOpenCycles_UseSameWindowInstance()
    {
        var shell = CreateShell(out var presenter, closeToTrayEnabled: true);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var closeArgs = new MainWindowClosingEventArgs();
            shell.HandleMainWindowClosing(closeArgs);
            Assert.True(closeArgs.Cancel);
            Assert.False(presenter.IsVisible);

            shell.ShowOrActivateMainWindowCore();
            Assert.True(presenter.IsVisible);
        }

        Assert.Equal(3, presenter.HideCallCount);
        Assert.Equal(3, presenter.ShowCallCount);
    }

    [Fact]
    public void ConfigureCloseToTray_WhenEnabled_SetsExplicitShutdownMode()
    {
        var shell = new ApplicationShell();
        var shutdownHost = new FakeShutdownHost();
        shell.Attach(shutdownHost, null!, new FakeMainWindowPresenter());

        shell.ConfigureCloseToTray(true);

        Assert.True(shell.IsCloseToTrayEnabled);
        Assert.Equal(ShutdownMode.OnExplicitShutdown, shutdownHost.ShutdownMode);
    }

    [Fact]
    public void ConfigureCloseToTray_WhenTrayUnavailable_LeavesDefaultShutdownMode()
    {
        var shell = new ApplicationShell();
        var shutdownHost = new FakeShutdownHost
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };
        shell.Attach(shutdownHost, null!, new FakeMainWindowPresenter());

        shell.ConfigureCloseToTray(false);

        Assert.False(shell.IsCloseToTrayEnabled);
        Assert.Equal(ShutdownMode.OnMainWindowClose, shutdownHost.ShutdownMode);
    }

    [Fact]
    public void ExitApplication_RequestsExplicitShutdownAndShutsDownApplication()
    {
        var shell = new ApplicationShell();
        var shutdownHost = new FakeShutdownHost();
        shell.Attach(shutdownHost, null!, new FakeMainWindowPresenter());
        shell.ConfigureCloseToTray(true);

        shell.ExitApplication();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, shutdownHost.ShutdownCallCount);
        Assert.Equal(MainWindowCloseOutcome.ProceedWithClose, shell.EvaluateCloseRequest());
    }

    private static ApplicationShell CreateShell(out FakeMainWindowPresenter presenter, bool closeToTrayEnabled)
    {
        var shell = new ApplicationShell();
        presenter = new FakeMainWindowPresenter();
        shell.Attach(new FakeShutdownHost(), null!, presenter);
        shell.ConfigureCloseToTray(closeToTrayEnabled);
        return shell;
    }

    private sealed class FakeMainWindowPresenter : IMainWindowPresenter
    {
        public bool IsVisible { get; set; } = true;

        public WindowState WindowState { get; set; } = WindowState.Normal;

        public int HideCallCount { get; private set; }

        public int ShowCallCount { get; private set; }

        public int ActivateCallCount { get; private set; }

        public event EventHandler<MainWindowClosingEventArgs>? Closing;

        public void Hide()
        {
            HideCallCount++;
            IsVisible = false;
        }

        public void Show()
        {
            ShowCallCount++;
            IsVisible = true;
        }

        public void Activate()
        {
            ActivateCallCount++;
        }
    }

    private sealed class FakeShutdownHost : IApplicationShutdownHost
    {
        public int ShutdownCallCount { get; private set; }

        public ShutdownMode ShutdownMode { get; set; } = ShutdownMode.OnMainWindowClose;

        public void Shutdown()
        {
            ShutdownCallCount++;
        }
    }
}
