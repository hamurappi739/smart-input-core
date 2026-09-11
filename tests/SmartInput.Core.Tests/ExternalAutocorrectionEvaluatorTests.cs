using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public sealed class ExternalAutocorrectionEvaluatorTests
{
    [Fact]
    public void AppliesUniqueKnownCandidate()
    {
        var dictionary = NewDictionary();
        var provider = new FakeProvider(new ExternalSpellCandidate("привет", 1, 950_000));

        var result = new ExternalAutocorrectionEvaluator().Evaluate(
            "превет", TypingLanguage.Russian, dictionary, provider);

        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal("привет", result.CandidateToken);
    }

    [Fact]
    public void PreservesExactKnownWord()
    {
        var result = new ExternalAutocorrectionEvaluator().Evaluate(
            "меня", TypingLanguage.Russian, NewDictionary(),
            new FakeProvider(new ExternalSpellCandidate("сеня", 1, 950_000)));

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void WaitsForEqualNearestCandidates()
    {
        var result = new ExternalAutocorrectionEvaluator().Evaluate(
            "превет", TypingLanguage.Russian, NewDictionary(),
            new FakeProvider(
                new ExternalSpellCandidate("привет", 1, 700_000),
                new ExternalSpellCandidate("прилет", 1, 650_000)));

        Assert.Equal(AutocorrectionRecommendation.Wait, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void UnscoredFallbackDoesNotBlockScoredCandidate()
    {
        var result = new ExternalAutocorrectionEvaluator().Evaluate(
            "превет", TypingLanguage.Russian, NewDictionary(),
            new FakeProvider(
                new ExternalSpellCandidate("привет", 1, 950_000),
                new ExternalSpellCandidate("преет", 1, 0)));

        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal("привет", result.CandidateToken);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("file_name")]
    [InlineData("abc123")]
    public void RejectsProtectedTokens(string token)
    {
        var result = new ExternalAutocorrectionEvaluator().Evaluate(
            token, TypingLanguage.English, NewDictionary(),
            new FakeProvider(new ExternalSpellCandidate("hello", 1, 1_000_000)));

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void PreservesCapitalization()
    {
        var result = new ExternalAutocorrectionEvaluator().Evaluate(
            "Превет", TypingLanguage.Russian, NewDictionary(),
            new FakeProvider(new ExternalSpellCandidate("привет", 1, 950_000)));

        Assert.Equal("Привет", result.CandidateToken);
    }

    [Fact]
    public void AcceptsWordKnownOnlyBySelectedExternalDictionary()
    {
        var result = new ExternalAutocorrectionEvaluator().Evaluate(
            "эксперимент",
            TypingLanguage.Russian,
            NewDictionary(),
            new FakeProvider(new ExternalSpellCandidate("эксперименты", 1, 950_000)));

        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal("эксперименты", result.CandidateToken);
    }

    private static TestAutocorrectDictionary NewDictionary()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.Russian, "привет", 0.95);
        dictionary.Add(TypingLanguage.Russian, "прилет", 0.65);
        dictionary.Add(TypingLanguage.Russian, "меня", 0.95);
        dictionary.Add(TypingLanguage.Russian, "сеня", 0.70);
        dictionary.Add(TypingLanguage.English, "hello", 0.95);
        return dictionary;
    }

    private sealed class FakeProvider(params ExternalSpellCandidate[] candidates)
        : IExternalSpellCorrectionProvider
    {
        public IReadOnlyList<ExternalSpellCandidate> FindCandidates(
            string token, TypingLanguage language, int maxEditDistance = 1) => candidates;
    }
}
