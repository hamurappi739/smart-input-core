using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using SmartInput.Core.Services;

namespace SmartInput.App.Services;

public interface IApplicationThemeService
{
    bool IsDarkTheme { get; }

    event Action? ThemeChanged;

    Task SetDarkThemeAsync(bool useDarkTheme, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the app appearance in sync with the persisted setting. The setting is
/// intentionally app-local: it does not modify the Windows appearance.
/// </summary>
public sealed class ApplicationThemeService : IApplicationThemeService
{
    private readonly ISettingsService _settingsService;
    private bool _isDarkTheme;

    public ApplicationThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.SettingsChanged += RefreshFromSettings;
        RefreshFromSettings();
    }

    public bool IsDarkTheme => _isDarkTheme;

    public event Action? ThemeChanged;

    public Task SetDarkThemeAsync(bool useDarkTheme, CancellationToken cancellationToken = default)
    {
        if (_settingsService.Current.UseDarkTheme == useDarkTheme)
        {
            ApplyThemeVariant(useDarkTheme);
            return Task.CompletedTask;
        }

        return _settingsService.UpdateAsync(
            settings => settings.UseDarkTheme = useDarkTheme,
            cancellationToken);
    }

    private void RefreshFromSettings()
    {
        var useDarkTheme = _settingsService.Current.UseDarkTheme;
        var changed = _isDarkTheme != useDarkTheme;
        _isDarkTheme = useDarkTheme;
        ApplyThemeVariant(useDarkTheme);

        if (changed)
        {
            ThemeChanged?.Invoke();
        }
    }

    private static void ApplyThemeVariant(bool useDarkTheme)
    {
        void Apply()
        {
            if (Application.Current is { } application)
            {
                application.RequestedThemeVariant = useDarkTheme
                    ? ThemeVariant.Dark
                    : ThemeVariant.Light;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
            return;
        }

        Dispatcher.UIThread.Post(Apply);
    }
}
