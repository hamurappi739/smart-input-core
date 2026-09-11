using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SmartInput.App.Services;

public interface IApplicationShutdownHost
{
    ShutdownMode ShutdownMode { get; set; }

    void Shutdown();
}

public interface IDesktopApplicationHost : IApplicationShutdownHost
{
    Window? MainWindow { get; set; }
}

public sealed class DesktopApplicationHost : IDesktopApplicationHost
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;

    public DesktopApplicationHost(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
    }

    public Window? MainWindow
    {
        get => _desktop.MainWindow;
        set => _desktop.MainWindow = value;
    }

    public ShutdownMode ShutdownMode
    {
        get => _desktop.ShutdownMode;
        set => _desktop.ShutdownMode = value;
    }

    public void Shutdown()
    {
        _desktop.Shutdown();
    }
}
