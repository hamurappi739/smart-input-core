using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Small representative smoke set for the opt-in external spelling mode.
/// Assertions intentionally cover outcomes, not diagnostic text dumps.
/// </summary>
public sealed class ExternalModeRepresentativeTests
{
    [Theory]
    [InlineData("превет", "привет", TypingLanguage.Russian)]
    [InlineData("мирр", "мир", TypingLanguage.Russian)]
    [InlineData("helo", "hello", TypingLanguage.English)]
    [InlineData("teh", "the", TypingLanguage.English)]
    [InlineData("adn", "and", TypingLanguage.English)]
    [InlineData("жызнь", "жизнь", TypingLanguage.Russian)]
    [InlineData("машына", "машина", TypingLanguage.Russian)]
    [InlineData("првет", "привет", TypingLanguage.Russian)]
    [InlineData("стрвнно", "странно", TypingLanguage.Russian)]
    [InlineData("ришения", "решения", TypingLanguage.Russian)]
    public void CommonTyposProduceExpectedCorrection(
        string input,
        string expected,
        TypingLanguage language)
    {
        var result = Evaluate(input, language);

        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal(expected, result.CandidateToken, ignoreCase: true);
    }

    [Theory]
    [InlineData("начала", TypingLanguage.Russian)]
    [InlineData("начало", TypingLanguage.Russian)]
    [InlineData("дает", TypingLanguage.Russian)]
    [InlineData("даст", TypingLanguage.Russian)]
    [InlineData("дают", TypingLanguage.Russian)]
    [InlineData("меня", TypingLanguage.Russian)]
    [InlineData("нас", TypingLanguage.Russian)]
    [InlineData("гавно", TypingLanguage.Russian)]
    [InlineData("душ", TypingLanguage.Russian)]
    [InlineData("самого", TypingLanguage.Russian)]
    public void CommonCorrectWordsRemainUntouched(string input, TypingLanguage language)
    {
        var result = Evaluate(input, language);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void ShortAmbiguousCyrillicTokenWaitsInsteadOfChoosingWrongWord()
    {
        var result = Evaluate("дла", TypingLanguage.Russian);

        Assert.Equal(AutocorrectionRecommendation.Wait, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Theory]
    [InlineData("https://example.com", TypingLanguage.English)]
    [InlineData("mail@example.com", TypingLanguage.English)]
    [InlineData("C:\\work\\file.txt", TypingLanguage.English)]
    [InlineData("snake_case", TypingLanguage.English)]
    public void ProtectedTokensPassThrough(string input, TypingLanguage language)
    {
        var result = Evaluate(input, language);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    private static AutocorrectionResult Evaluate(string input, TypingLanguage language)
    {
        var dictionary = new TestAutocorrectDictionary();
        foreach (var ((entryLanguage, word), frequency) in StarterAutocorrectLexicon.Entries)
        {
            dictionary.Add(entryLanguage, word, frequency);
        }

        var provider = new CompositeExternalSpellCorrectionProvider(
            new SymSpellSpellCorrectionProvider(),
            new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));

        return new ExternalAutocorrectionEvaluator().Evaluate(
            input,
            language,
            dictionary,
            provider,
            new AutocorrectionOptions { UseExternalProvider = true });
    }
}
