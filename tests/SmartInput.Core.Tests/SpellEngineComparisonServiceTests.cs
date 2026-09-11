using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public sealed class SpellEngineComparisonServiceTests
{
    [Fact]
    public void ComparisonRetainsOnlyAggregatePrivacySafeCounters()
    {
        var dictionary = BuildDictionary();
        var cases = new[]
        {
            new SpellEngineComparisonCase("привет", TypingLanguage.Russian),
            new SpellEngineComparisonCase("превет", TypingLanguage.Russian),
            new SpellEngineComparisonCase("https://example.com", TypingLanguage.English),
            new SpellEngineComparisonCase("file_name", TypingLanguage.English),
        };

        var summary = new SpellEngineComparisonService().Compare(
            cases,
            new AutocorrectionService(),
            new ExternalAutocorrectionEvaluator(),
            new SymSpellSpellCorrectionProvider(),
            dictionary);

        Assert.Equal(4, summary.TotalCases);
        Assert.Equal(1, summary.ExactWordsChecked);
        Assert.Equal(2, summary.ProtectedTokensChecked);
        Assert.Equal(0, summary.ExactWordsChangedByExternal);
        Assert.Equal(0, summary.ProtectedTokensChangedByExternal);
        Assert.DoesNotContain(
            summary.GetType().GetProperties(),
            property => property.PropertyType == typeof(string));
    }

    [Fact]
    public void LargeMutationCorpusNeverChangesExactWords()
    {
        var dictionary = BuildDictionary();
        var words = StarterAutocorrectLexicon.Entries
            .Where(entry => entry.Key.Word.Length >= 4)
            .Take(1_200)
            .Select(entry => new SpellEngineComparisonCase(entry.Key.Word, entry.Key.Language))
            .ToArray();

        var mutations = words
            .Concat(words.SelectMany(CreateMutations))
            .Concat(new[]
            {
                new SpellEngineComparisonCase("https://example.com", TypingLanguage.English),
                new SpellEngineComparisonCase("mail@example.com", TypingLanguage.English),
                new SpellEngineComparisonCase("C:\\work\\file.txt", TypingLanguage.English),
                new SpellEngineComparisonCase("snake_case", TypingLanguage.English),
            })
            .ToArray();

        var summary = new SpellEngineComparisonService().Compare(
            mutations,
            new AutocorrectionService(),
            new ExternalAutocorrectionEvaluator(),
            new SymSpellSpellCorrectionProvider(),
            dictionary);

        Assert.True(summary.TotalCases >= 2_000);
        Assert.True(summary.ExactWordsChecked >= 1_000);
        Assert.Equal(0, summary.ExactWordsChangedByExternal);
        Assert.Equal(0, summary.ProtectedTokensChangedByExternal);
        Assert.True(summary.ExternalMaxMilliseconds >= 0.0);
    }

    private static IEnumerable<SpellEngineComparisonCase> CreateMutations(
        SpellEngineComparisonCase item)
    {
        var word = item.Token;
        yield return new SpellEngineComparisonCase(word[..1] + word, item.Language);
        yield return new SpellEngineComparisonCase(word.Remove(1, 1), item.Language);
        yield return new SpellEngineComparisonCase(
            word[..1] + word[2] + word[1] + word[3..],
            item.Language);
    }

    private static TestAutocorrectDictionary BuildDictionary()
    {
        var dictionary = new TestAutocorrectDictionary();
        foreach (var ((language, word), frequency) in StarterAutocorrectLexicon.Entries)
        {
            dictionary.Add(language, word, frequency);
        }

        return dictionary;
    }
}
