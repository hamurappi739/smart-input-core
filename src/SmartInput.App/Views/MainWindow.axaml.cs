using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace SmartInput.App.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan ThemeRowDuration = TimeSpan.FromMilliseconds(72);
    private static readonly TimeSpan ThemeRowGap = TimeSpan.FromMilliseconds(12);
    private static readonly TimeSpan ThemeFadeDuration = TimeSpan.FromMilliseconds(160);
    private CancellationTokenSource? _themeTransitionCancellation;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (Application.Current is { } application)
        {
            application.PropertyChanged += OnApplicationPropertyChanged;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Application.Current is { } application)
        {
            application.PropertyChanged -= OnApplicationPropertyChanged;
        }

        _themeTransitionCancellation?.Cancel();
        base.OnClosed(e);
    }

    private void OnApplicationPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Application.ActualThemeVariantProperty)
        {
            _ = PlayThemeWashAsync();
        }
    }

    private async Task PlayThemeWashAsync()
    {
        var rows = new[]
        {
            ThemeWashRow0, ThemeWashRow1, ThemeWashRow2, ThemeWashRow3, ThemeWashRow4,
            ThemeWashRow5, ThemeWashRow6, ThemeWashRow7, ThemeWashRow8, ThemeWashRow9,
        };

        if (IsReducedMotionRequested())
        {
            ThemeWashOverlay.IsVisible = false;
            return;
        }

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _themeTransitionCancellation,
            cancellation);
        previous?.Cancel();

        var newThemeIsDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var previousThemeColor = newThemeIsDark
            ? Color.Parse("#F5F5F7")
            : Color.Parse("#111318");
        var previousThemeBrush = new SolidColorBrush(previousThemeColor);

        foreach (var row in rows)
        {
            row.Background = previousThemeBrush;
            row.Opacity = 1;
        }

        ThemeWashOverlay.Opacity = 1;
        ThemeWashOverlay.IsVisible = true;

        try
        {
            foreach (var row in rows)
            {
                await AnimateOpacityAsync(row, 1, 0, ThemeRowDuration, cancellation.Token);
                await Task.Delay(ThemeRowGap, cancellation.Token);
            }

            await AnimateOpacityAsync(
                ThemeWashOverlay,
                1,
                0,
                ThemeFadeDuration,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A new theme click interrupts the old wave immediately.
        }
        finally
        {
            if (ReferenceEquals(_themeTransitionCancellation, cancellation))
            {
                ThemeWashOverlay.IsVisible = false;
                ThemeWashOverlay.Opacity = 1;
                _themeTransitionCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private static async Task AnimateOpacityAsync(
        Avalonia.Controls.Control control,
        double from,
        double to,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        control.Opacity = from;

        while (stopwatch.Elapsed < duration)
        {
            await Task.Delay(16, cancellationToken);
            var progress = Math.Clamp(
                stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds,
                0,
                1);
            // Smooth-step easing keeps the row movement soft without adding
            // a layout animation or an uninterruptible storyboard.
            var eased = progress * progress * (3 - (2 * progress));
            control.Opacity = from + ((to - from) * eased);
        }

        control.Opacity = to;
    }

    private static bool IsReducedMotionRequested()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var clientAreaAnimationEnabled = true;
        return NativeMethods.SystemParametersInfo(
                   NativeMethods.SpiGetClientAreaAnimation,
                   0,
                   ref clientAreaAnimationEnabled,
                   0)
               && !clientAreaAnimationEnabled;
    }

    private static class NativeMethods
    {
        internal const uint SpiGetClientAreaAnimation = 0x1042;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            ref bool value,
            uint updateOptions);
    }
}
