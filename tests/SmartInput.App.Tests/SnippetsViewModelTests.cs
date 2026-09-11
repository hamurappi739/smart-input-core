using SmartInput.App.ViewModels;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;

namespace SmartInput.App.Tests;

public class SnippetsViewModelTests
{
    [Fact]
    public async Task SaveSnippetAsync_AddsAndSelectsLocalSnippet()
    {
        var persistence = new InMemorySnippetPersistence();
        var service = new SnippetService(persistence);
        var viewModel = new SnippetsViewModel(new FakeSettingsService(), service);

        await WaitUntilAsync(() => !viewModel.IsBusy);
        viewModel.TriggerText = "/адрес";
        viewModel.ReplacementText = "ул. Ленина, 1";
        viewModel.SelectedLanguageOption = "Русский";

        await viewModel.SaveSnippetCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSnippets);
        Assert.Single(service.Snippets);
        Assert.Equal("/адрес", service.Snippets[0].Trigger);
        Assert.Equal("ул. Ленина, 1", service.Snippets[0].Replacement);
        Assert.NotNull(viewModel.SelectedSnippet);
        Assert.Equal("Изменить шаблон", viewModel.EditorTitle);
    }

    [Fact]
    public async Task SaveSnippetAsync_InvalidInput_ShowsActionableValidation()
    {
        var service = new SnippetService(new InMemorySnippetPersistence());
        var viewModel = new SnippetsViewModel(new FakeSettingsService(), service);
        await WaitUntilAsync(() => !viewModel.IsBusy);

        viewModel.TriggerText = "две части";
        viewModel.ReplacementText = "текст";

        await viewModel.SaveSnippetCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasValidationErrors);
        Assert.Contains("пробел", viewModel.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Проверьте", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(service.Snippets);
    }

    [Fact]
    public async Task ToggleAndDelete_SelectedSnippetUpdatesStoreAndEmptyState()
    {
        var persistence = new InMemorySnippetPersistence();
        var service = new SnippetService(persistence);
        await service.LoadAsync();
        _ = await service.AddAsync(new SnippetDefinition
        {
            Trigger = "/sig",
            Replacement = "С уважением",
            IsEnabled = true,
        });

        var viewModel = new SnippetsViewModel(new FakeSettingsService(), service);
        await WaitUntilAsync(() => !viewModel.IsBusy && viewModel.HasSnippets);
        var item = Assert.Single(viewModel.Snippets);

        await viewModel.ToggleEnabledCommand.ExecuteAsync(item);

        Assert.False(service.Snippets[0].IsEnabled);
        Assert.Equal("Включить", viewModel.Snippets[0].ToggleLabel);

        await viewModel.DeleteSnippetCommand.ExecuteAsync(null);

        Assert.Empty(service.Snippets);
        Assert.False(viewModel.HasSnippets);
        Assert.Null(viewModel.SelectedSnippet);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(predicate(), "View model did not finish loading in time.");
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            SettingsChanged?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySnippetPersistence : ISnippetPersistence
    {
        private IReadOnlyList<SnippetDefinition> _snippets = [];

        public Task<IReadOnlyList<SnippetDefinition>> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snippets);

        public Task SaveAsync(
            IReadOnlyList<SnippetDefinition> snippets,
            CancellationToken cancellationToken = default)
        {
            _snippets = snippets.ToList();
            return Task.CompletedTask;
        }
    }
}
