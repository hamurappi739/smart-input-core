using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public class AutocorrectionTests
{
    private readonly AutocorrectionService _service = new();
    private readonly TestAutocorrectDictionary _dictionary = CreateDefaultDictionary();

    [Fact]
    public void Evaluate_RussianTypo_PrivetSuggestPrivet()
    {
        var result = _service.Evaluate("превет", TypingLanguage.Russian, _dictionary);

        Assert.Equal("превет", result.OriginalToken);
        Assert.Equal("привет", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= AutocorrectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_EnglishTypo_HeloSuggestHello()
    {
        var result = _service.Evaluate("helo", TypingLanguage.English, _dictionary);

        Assert.Equal("hello", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_MissingCharacter_IsDetected()
    {
        var result = _service.Evaluate("helo", TypingLanguage.English, _dictionary);

        Assert.Equal("hello", result.CandidateToken);
    }

    [Fact]
    public void Evaluate_ExtraCharacter_IsDetected()
    {
        var result = _service.Evaluate("helloo", TypingLanguage.English, _dictionary);

        Assert.Equal("hello", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_TransposedCharacters_IsDetected()
    {
        var result = _service.Evaluate("teh", TypingLanguage.English, _dictionary);

        Assert.Equal("the", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_RepeatedCharacterCleanup_IsDetected()
    {
        var result = _service.Evaluate("helllo", TypingLanguage.English, _dictionary);

        Assert.Equal("hello", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_AdjacentKeySubstitution_IsDetected()
    {
        var dictionary = CreateDefaultDictionary();
        dictionary.Add(TypingLanguage.English, "hello", 1.0);

        var result = _service.Evaluate("helko", TypingLanguage.English, dictionary);

        Assert.Equal("hello", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_CorrectEnglishWord_ReturnsNoChange()
    {
        var result = _service.Evaluate("hello", TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
        Assert.True(result.ConfidenceScore < AutocorrectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_CorrectRussianWord_ReturnsNoChange()
    {
        var result = _service.Evaluate("привет", TypingLanguage.Russian, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void Evaluate_Capitalization_IsPreservedForAllUpperInput()
    {
        var result = _service.Evaluate("HELO", TypingLanguage.English, _dictionary);

        Assert.Equal("HELLO", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void CapitalizationPreserver_TitleCase_IsApplied()
    {
        var corrected = CapitalizationPreserver.Apply("Helo", "hello");

        Assert.Equal("Hello", corrected);
    }

    [Fact]
    public void Evaluate_ShortAmbiguousToken_ReturnsWaitOrNoChange()
    {
        var result = _service.Evaluate("he", TypingLanguage.English, _dictionary);

        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Theory]
    [InlineData("Cursor")]
    [InlineData("GitHub")]
    [InlineData("myVariableName")]
    public void Evaluate_ProtectedTokens_ReturnNoChange(string token)
    {
        var result = _service.Evaluate(token, TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_Url_ReturnsNoChange()
    {
        var result = _service.Evaluate("https://example.com", TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_EmailLikeToken_ReturnsNoChange()
    {
        var result = _service.Evaluate("user@example.com", TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_FilePath_ReturnsNoChange()
    {
        var result = _service.Evaluate(@"C:\Projects\file.txt", TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_MixedLanguageToken_ReturnsNoChange()
    {
        var result = _service.Evaluate("helloмир", TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_EmptyInput_ReturnsNoChange()
    {
        var result = _service.Evaluate(string.Empty, TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_UnsupportedLanguageScript_ReturnsNoChange()
    {
        var result = _service.Evaluate("привет", TypingLanguage.English, _dictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_Ranking_IsDeterministic()
    {
        var first = _service.Evaluate("helo", TypingLanguage.English, _dictionary);
        var second = _service.Evaluate("helo", TypingLanguage.English, _dictionary);

        Assert.Equal(first.CandidateToken, second.CandidateToken);
        Assert.Equal(first.ConfidenceScore, second.ConfidenceScore);
        Assert.Equal(first.Recommendation, second.Recommendation);
    }

    [Fact]
    public void Evaluate_BelowCandidateThreshold_ReturnsWaitOrNoChange()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.English, "hello", 0.01);

        var result = _service.Evaluate(
            "hxlo",
            TypingLanguage.English,
            dictionary,
            new AutocorrectionOptions
            {
                CandidateThreshold = 0.99,
                WaitThreshold = 0.10,
            });

        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_HigherFrequencyCandidateWinsDeterministically()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.English, "hello", 1.0);
        dictionary.Add(TypingLanguage.English, "helmo", 0.01);

        var result = _service.Evaluate("helko", TypingLanguage.English, dictionary);

        Assert.Equal("hello", result.CandidateToken);
    }

    private static TestAutocorrectDictionary CreateDefaultDictionary()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.English, "hello", 1.0);
        dictionary.Add(TypingLanguage.English, "the", 0.95);
        dictionary.Add(TypingLanguage.English, "help", 0.70);
        dictionary.Add(TypingLanguage.English, "world", 0.80);
        dictionary.Add(TypingLanguage.Russian, "привет", 1.0);
        dictionary.Add(TypingLanguage.Russian, "мир", 0.80);
        return dictionary;
    }
}

internal sealed class TestAutocorrectDictionary : IAutocorrectDictionary
{
    private readonly Dictionary<(TypingLanguage Language, string Word), double> _entries = new();
    private readonly HashSet<(TypingLanguage Language, string Word)> _userEntries = new();

    public void Add(TypingLanguage language, string word, double frequency, bool isUserEntry = false)
    {
        var normalized = word.ToLowerInvariant();
        _entries[(language, normalized)] = frequency;

        if (isUserEntry)
        {
            _userEntries.Add((language, normalized));
        }
    }

    public bool Contains(string word, TypingLanguage language)
    {
        return _entries.ContainsKey((language, word.ToLowerInvariant()));
    }

    public double GetFrequency(string word, TypingLanguage language)
    {
        return _entries.TryGetValue((language, word.ToLowerInvariant()), out var frequency)
            ? frequency
            : 0.0;
    }

    public bool IsNeverAutocorrect(string word, TypingLanguage language) => false;

    public bool IsUserDictionaryEntry(string word, TypingLanguage language)
    {
        return _userEntries.Contains((language, word.ToLowerInvariant()));
    }
}
