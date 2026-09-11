using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

[Trait("Category", "SentenceLanguageContext")]
public class SentenceLanguageContextTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly JointCorrectionDecisionService _joint;

    public SentenceLanguageContextTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(),
            converter);
    }

    [Theory]
    [InlineData("начала")]
    [InlineData("начало")]
    [InlineData("дает")]
    [InlineData("даёт")]
    [InlineData("даст")]
    [InlineData("дают")]
    [InlineData("дела")]
    [InlineData("нас")]
    [InlineData("меня")]
    [InlineData("неизвестное")]
    [InlineData("неизвестно")]
    public void ValidRussianForms_StayUnchanged_InRussianSentenceContext(string token)
    {
        var hint = BuildRussianHint();
        var result = _joint.Evaluate(token, _dictionary, true, true, languageHint: hint);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Fact]
    public void Nen_BecomesTut_InStrongRussianContext()
    {
        var hint = BuildRussianHint();
        var result = _joint.Evaluate("nen", _dictionary, true, true, languageHint: hint);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal("тут", result.ReplacementToken);
    }

    [Fact]
    public void UltraFrequentShortEnglishTarget_BypassesAmbiguousRussianSpelling()
    {
        Assert.False(LayoutServiceWordWhitelist.TryGetReplacement("ыру", out _, out _));

        var result = _joint.Evaluate("ыру", _dictionary, true, true);

        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal("she", result.ReplacementToken);
        Assert.Equal(CorrectionKind.Layout, result.Kind);
    }

    [Fact]
    public void KnownRussianShortCollision_StaysWithoutEnglishContext()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateProductionLikeDictionary();
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(),
            converter);

        var result = joint.Evaluate("рук", dictionary, true, true);

        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Fact]
    public void KnownRussianShortCollision_BecomesEnglishAfterPriorEnglishToken()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateProductionLikeDictionary();
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(),
            converter);
        var hint = new SentenceLanguageHint(TypingLanguage.English, 0, 1, 1, 3);

        var result = joint.Evaluate("рук", dictionary, true, true, languageHint: hint);

        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal("her", result.ReplacementToken);
        Assert.Equal(CorrectionKind.Layout, result.Kind);
    }

    [Fact]
    public void KnownRussianShortCollision_StaysInRussianContext()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateProductionLikeDictionary();
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(),
            converter);

        var result = joint.Evaluate(
            "рук",
            dictionary,
            true,
            true,
            languageHint: BuildRussianHint());

        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Fact]
    public void ProtectedNen_StaysInEnglishContext()
    {
        var store = new FakeUserStore();
        store.AddOrUpdate(new UserAutocorrectDictionaryEntry
        {
            Word = "Nen",
            Language = TypingLanguage.English,
            NeverAutocorrect = true,
        });
        var dictionary = new CompositeAutocorrectDictionary(store);
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(),
            converter);

        var hint = new SentenceLanguageHint(TypingLanguage.English, 0, 4, 4, 16);
        var result = joint.Evaluate("Nen", dictionary, true, true, languageHint: hint);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Fact]
    public void Buffer_ClearsOnPolicyBlockAndRespectsBounds()
    {
        var buffer = new SentenceLanguageContextBuffer(maxTokens: 4, maxCharacters: 40);
        buffer.RecordCompletedToken("для");
        buffer.RecordCompletedToken("начала");
        buffer.RecordCompletedToken("работы");
        buffer.RecordCompletedToken("сейчас");
        buffer.RecordCompletedToken("снова");
        Assert.True(buffer.TokenCount <= 4);
        Assert.True(buffer.CharacterCount <= 40);

        buffer.SetCollectionEnabled(false);
        Assert.True(buffer.IsEmpty);
    }

    private static SentenceLanguageHint BuildRussianHint()
    {
        return new SentenceLanguageHint(TypingLanguage.Russian, 5, 0, 5, 24);
    }

    private sealed class FakeUserStore : IUserAutocorrectDictionaryStore
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
            _entries.RemoveAll(existing =>
                existing.Language == entry.Language
                && string.Equals(existing.Word, entry.Word, StringComparison.OrdinalIgnoreCase));
            _entries.Add(entry);
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(existing =>
                existing.Language == language
                && string.Equals(existing.Word, word, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }
    }
}
