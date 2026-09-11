using SmartInput.App.Services;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.Tests;

[Collection(AppTestCollection.Name)]
public class ApplicationThemeServiceTests
{
    [Fact]
    public async Task SetDarkThemeAsync_PersistsAndPublishesTheNewTheme()
    {
        var settings = new FakeSettingsService(new AppSettings { UseDarkTheme = false });
        var service = new ApplicationThemeService(settings);
        var notifications = 0;
        service.ThemeChanged += () => notifications++;

        await service.SetDarkThemeAsync(true);

        Assert.True(settings.Current.UseDarkTheme);
        Assert.True(service.IsDarkTheme);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task ExternalSettingsChange_UpdatesTheThemeState()
    {
        var settings = new FakeSettingsService(new AppSettings { UseDarkTheme = false });
        var service = new ApplicationThemeService(settings);

        await settings.UpdateAsync(value => value.UseDarkTheme = true);

        Assert.True(service.IsDarkTheme);
    }

    [Fact]
    public async Task SettingTheCurrentValue_DoesNotWriteOrNotifyAgain()
    {
        var settings = new FakeSettingsService(new AppSettings { UseDarkTheme = true });
        var service = new ApplicationThemeService(settings);
        var notifications = 0;
        service.ThemeChanged += () => notifications++;

        await service.SetDarkThemeAsync(true);

        Assert.Equal(0, settings.UpdateCount);
        Assert.Equal(0, notifications);
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public event Action? SettingsChanged;

        public AppSettings Current { get; } = settings;

        public int UpdateCount { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            update(Current);
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }
    }
}
