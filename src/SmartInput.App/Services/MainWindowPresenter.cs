using Avalonia.Controls;

namespace SmartInput.App.Services;

public sealed class MainWindowClosingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}

public interface IMainWindowPresenter
{
    bool IsVisible { get; }

    WindowState WindowState { get; set; }

    void Hide();

    void Show();

    void Activate();

    event EventHandler<MainWindowClosingEventArgs>? Closing;
}

public sealed class AvaloniaMainWindowPresenter : IMainWindowPresenter
{
    private readonly Window _window;

    public AvaloniaMainWindowPresenter(Window window)
    {
        _window = window;
        _window.Closing += OnWindowClosing;
    }

    public bool IsVisible => _window.IsVisible;

    public WindowState WindowState
    {
        get => _window.WindowState;
        set => _window.WindowState = value;
    }

    public event EventHandler<MainWindowClosingEventArgs>? Closing;

    public void Hide()
    {
        _window.Hide();
    }

    public void Show()
    {
        _window.Show();
    }

    public void Activate()
    {
        _window.Activate();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        var args = new MainWindowClosingEventArgs
        {
            Cancel = e.Cancel,
        };

        Closing?.Invoke(this, args);
        e.Cancel = args.Cancel;
    }
}
