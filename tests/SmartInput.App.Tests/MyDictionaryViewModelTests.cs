using SmartInput.App.Services;
using SmartInput.App.ViewModels;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;

namespace SmartInput.App.Tests;

public class MyDictionaryViewModelTests
{
    [Fact]
    public async Task AddWordAsync_PersistsEntryToStore()
    {
        var dictionaryStore = new UserAutocorrectDictionaryStore(new InMemoryUserDictionaryPersistence());
        var rejectionStore = new InMemoryCorrectionRejectionLearningStore();
        var viewModel = new MyDictionaryViewModel(
            dictionaryStore,
            rejectionStore,
            new CorrectionRejectionPolicy(rejectionStore));

        viewModel.NewWord = "Cursor";
        viewModel.SelectedLanguageOption = "Английский";
        viewModel.NeverAutocorrect = true;

        await viewModel.AddWordCommand.ExecuteAsync(null);
        await Task.Delay(100);

        Assert.Single(viewModel.Words);
        Assert.Equal("Cursor", viewModel.Words[0].Word);
        Assert.Equal("Английский", viewModel.Words[0].LanguageLabel);
        Assert.True(viewModel.Words[0].NeverAutocorrect);
    }

    [Fact]
    public async Task AllowAgainAsync_RemovesRejectedCorrectionFromList()
    {
        var dictionaryStore = new UserAutocorrectDictionaryStore(new InMemoryUserDictionaryPersistence());
        var rejectionStore = new InMemoryCorrectionRejectionLearningStore();
        await rejectionStore.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });

        var viewModel = new MyDictionaryViewModel(
            dictionaryStore,
            rejectionStore,
            new CorrectionRejectionPolicy(rejectionStore));

        await Task.Delay(200);

        Assert.Single(viewModel.RejectedCorrections);
        var item = viewModel.RejectedCorrections[0];
        await viewModel.AllowAgainCommand.ExecuteAsync(item);
        await Task.Delay(100);

        Assert.Empty(viewModel.RejectedCorrections);
    }

    private sealed class InMemoryUserDictionaryPersistence : IUserAutocorrectDictionaryPersistence
    {
        private IReadOnlyList<UserAutocorrectDictionaryEntry> _entries = [];

        public Task<IReadOnlyList<UserAutocorrectDictionaryEntry>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_entries);
        }

        public Task SaveAsync(
            IReadOnlyList<UserAutocorrectDictionaryEntry> entries,
            CancellationToken cancellationToken = default)
        {
            _entries = entries.ToList();
            return Task.CompletedTask;
        }
    }
}
