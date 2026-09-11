using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_UsesDefaultsWhenPersistenceReturnsNull()
    {
        var persistence = new FakeSettingsPersistence(null);
        var service = new SettingsService(persistence);

        await service.LoadAsync();

        Assert.True(service.Current.IsEnabled);
        Assert.False(service.Current.AutocorrectEnabled);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var persistence = new FakeSettingsPersistence(null);
        var service = new SettingsService(persistence);
        await service.LoadAsync();

        await service.UpdateAsync(settings => settings.IsEnabled = false);

        Assert.False(service.Current.IsEnabled);
        Assert.False(persistence.LastSaved!.IsEnabled);
    }

    [Fact]
    public async Task UpdateAsync_RaisesSettingsChanged()
    {
        var persistence = new FakeSettingsPersistence(null);
        var service = new SettingsService(persistence);
        await service.LoadAsync();

        var changeCount = 0;
        service.SettingsChanged += () => changeCount++;

        await service.UpdateAsync(settings => settings.PredictionEnabled = false);

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task ExternalSpellingEngineSetting_PersistsAsOptInOnly()
    {
        var persistence = new FakeSettingsPersistence(null);
        var service = new SettingsService(persistence);
        await service.LoadAsync();

        Assert.False(service.Current.ExternalSpellingEngineEnabled);
        await service.UpdateAsync(settings => settings.ExternalSpellingEngineEnabled = true);

        Assert.True(service.Current.ExternalSpellingEngineEnabled);
        Assert.True(persistence.LastSaved!.ExternalSpellingEngineEnabled);
    }

    private sealed class FakeSettingsPersistence : ISettingsPersistence
    {
        public FakeSettingsPersistence(AppSettings? initial)
        {
            Stored = initial;
        }

        public AppSettings? Stored { get; private set; }

        public AppSettings? LastSaved { get; private set; }

        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Stored);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            LastSaved = settings;
            Stored = settings;
            return Task.CompletedTask;
        }
    }
}
