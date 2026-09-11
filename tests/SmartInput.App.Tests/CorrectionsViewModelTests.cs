using SmartInput.App.ViewModels;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.Tests;

public class CorrectionsViewModelTests
{
    [Fact]
    public async Task SyncFromSettings_LoadsPersistedCorrectionFlags()
    {
        var settings = new FakeSettingsService(new AppSettings
        {
            AutomaticLayoutEnabled = false,
            AutocorrectEnabled = false,
            CapitalizationEnabled = true,
            PunctuationEnabled = true,
            PredictionEnabled = false,
        });

        var viewModel = new CorrectionsViewModel(settings);
        await Task.Delay(50);

        Assert.False(viewModel.AutomaticLayoutEnabled);
        Assert.False(viewModel.AutocorrectEnabled);
        Assert.True(viewModel.CapitalizationEnabled);
        Assert.True(viewModel.PunctuationEnabled);
        Assert.False(viewModel.PredictionEnabled);
    }

    [Fact]
    public async Task AutomaticLayoutToggle_PersistsChange()
    {
        var settings = new FakeSettingsService(new AppSettings { AutomaticLayoutEnabled = true });
        var viewModel = new CorrectionsViewModel(settings);
        await Task.Delay(50);

        viewModel.AutomaticLayoutEnabled = false;
        await Task.Delay(50);

        Assert.False(settings.Current.AutomaticLayoutEnabled);
    }

    private sealed class FakeSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            return Task.CompletedTask;
        }
    }
}
