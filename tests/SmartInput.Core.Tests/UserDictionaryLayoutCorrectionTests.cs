using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public class UserDictionaryLayoutCorrectionTests
{
    [Fact]
    public async Task UnknownMush_CorrectsToVeoWithoutUserDictionary()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("мущ", replacement.LastOriginal);
        Assert.Equal("veo", replacement.LastReplacement);
    }

    [Fact]
    public async Task UserDictionaryEntry_Veo_TypesMushCorrectsToVeo()
    {
        var dictionary = await CreateDictionaryWithWordAsync("veo", TypingLanguage.English);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            dictionary: dictionary);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("мущ", replacement.LastOriginal);
        Assert.Equal("veo", replacement.LastReplacement);
    }

    [Fact]
    public async Task UserDictionaryEntry_Veo_TypesMushSpaceThreeCorrectsToVeoSpaceThree()
    {
        var dictionary = await CreateDictionaryWithWordAsync("veo", TypingLanguage.English);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            dictionary: dictionary);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());
        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('3'));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("мущ", replacement.LastOriginal);
        Assert.Equal("veo", replacement.LastReplacement);
    }

    [Fact]
    public async Task NeverAutocorrectUserEntry_DoesNotTriggerLayoutCorrection()
    {
        var dictionary = await CreateDictionaryWithWordAsync(
            "veo",
            TypingLanguage.English,
            neverAutocorrect: true);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            dictionary: dictionary);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task KnownRussianWord_IsNotChangedByUserDictionaryEvidence()
    {
        var dictionary = await CreateDictionaryWithWordAsync("veo", TypingLanguage.English);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            dictionary: dictionary);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "привет");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public void WrongLayoutDetection_UserDictionaryVeo_SuggestsVeoForMush()
    {
        var store = new MutableUserAutocorrectDictionaryStore();
        store.AddOrUpdate(new UserAutocorrectDictionaryEntry
        {
            Word = "veo",
            Language = TypingLanguage.English,
        });

        var service = new WrongLayoutDetectionService(
            new KeyboardLayoutConverter(),
            new CompositeAutocorrectDictionary(store));

        var result = service.Evaluate("мущ", ActiveLanguageSet.EnglishAndRussian);

        Assert.Equal("veo", result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    private static Task<CompositeAutocorrectDictionary> CreateDictionaryWithWordAsync(
        string word,
        TypingLanguage language,
        bool neverAutocorrect = false)
    {
        var store = new MutableUserAutocorrectDictionaryStore();
        store.AddOrUpdate(new UserAutocorrectDictionaryEntry
        {
            Word = word,
            Language = language,
            NeverAutocorrect = neverAutocorrect,
        });

        return Task.FromResult(new CompositeAutocorrectDictionary(store));
    }

    private static TokenInputEvent CreateSpaceBoundary()
    {
        return TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57));
    }

    private sealed class MutableUserAutocorrectDictionaryStore : IUserAutocorrectDictionaryStore
    {
        private readonly List<UserAutocorrectDictionaryEntry> _entries = [];

        public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries => _entries;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
        {
            AddOrUpdate(entry);
            return Task.CompletedTask;
        }

        public void AddOrUpdate(UserAutocorrectDictionaryEntry entry)
        {
            var index = _entries.FindIndex(existing =>
                existing.Language == entry.Language
                && string.Equals(existing.Word, entry.Word, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _entries[index] = entry;
            }
            else
            {
                _entries.Add(entry);
            }
        }

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(existing =>
                existing.Language == language
                && string.Equals(existing.Word, word, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
