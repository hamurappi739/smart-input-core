using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public class PredictionTests
{
    private readonly PredictionService _service = new(new StarterLocalPredictionModel());

    [Fact]
    public void Predict_EnglishNextWord_ReturnsSuggestion()
    {
        var result = Predict("how are ", TypingLanguage.English);

        Assert.Equal(PredictionRecommendation.Suggestion, result.Recommendation);
        Assert.Equal("you", result.SuggestedContinuation);
        Assert.Equal(1, result.TokenCount);
        Assert.True(result.Confidence >= PredictionOptions.DefaultSuggestionConfidenceThreshold);
    }

    [Fact]
    public void Predict_RussianNextWord_ReturnsSuggestion()
    {
        var result = Predict("как ", TypingLanguage.Russian);

        Assert.Equal(PredictionRecommendation.Suggestion, result.Recommendation);
        Assert.Equal("дела", result.SuggestedContinuation);
        Assert.Equal(1, result.TokenCount);
    }

    [Fact]
    public void Predict_PrefixFiltering_ReturnsSuffixContinuation()
    {
        var result = _service.Predict(new PredictionRequest
        {
            Context = "how are ",
            ActiveLanguage = TypingLanguage.English,
            CurrentWordPrefix = "y",
        });

        Assert.Equal(PredictionRecommendation.Suggestion, result.Recommendation);
        Assert.Equal("ou", result.SuggestedContinuation);
        Assert.Equal(1, result.TokenCount);
    }

    [Fact]
    public void Predict_FrequencyRanking_PrefersHigherScore()
    {
        var result = Predict("this is ", TypingLanguage.English);

        Assert.Equal("a", result.SuggestedContinuation);
        Assert.Equal(PredictionRecommendation.Suggestion, result.Recommendation);
    }

    [Fact]
    public void Predict_DeterministicTieBreaking_UsesAlphabeticalOrder()
    {
        var service = new PredictionService(new FixedPredictionModel(
        [
            new PredictionModelCandidate { Token = "zebra", FrequencyScore = 0.70 },
            new PredictionModelCandidate { Token = "alpha", FrequencyScore = 0.70 },
        ]));

        var result = service.Predict(new PredictionRequest
        {
            Context = "hello ",
            ActiveLanguage = TypingLanguage.English,
        });

        Assert.Equal("alpha", result.SuggestedContinuation);
    }

    [Fact]
    public void Predict_AfterTrailingSpace_DoesNotDuplicateSpacing()
    {
        var result = Predict("hello ", TypingLanguage.English);

        Assert.Equal("world", result.SuggestedContinuation);
        Assert.NotNull(result.SuggestedContinuation);
        Assert.DoesNotContain(' ', result.SuggestedContinuation!);
    }

    [Fact]
    public void Predict_AfterClausePunctuation_InsertsLeadingSpace()
    {
        var service = new PredictionService(new FixedPredictionModel(
        [
            new PredictionModelCandidate { Token = "world", FrequencyScore = 0.80 },
        ]));

        var result = service.Predict(new PredictionRequest
        {
            Context = "hello,",
            ActiveLanguage = TypingLanguage.English,
        });

        Assert.Equal(" world", result.SuggestedContinuation);
    }

    [Fact]
    public void Predict_EmptyContext_ReturnsNoSuggestion()
    {
        var result = Predict(string.Empty, TypingLanguage.English);

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
        Assert.Null(result.SuggestedContinuation);
        Assert.Equal(0, result.TokenCount);
    }

    [Fact]
    public void Predict_ShortContext_ReturnsNoSuggestion()
    {
        var result = _service.Predict(new PredictionRequest
        {
            Context = "a",
            ActiveLanguage = TypingLanguage.English,
            Options = new PredictionOptions
            {
                MinContextCharacters = 4,
                MinContextWords = 1,
            },
        });

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Fact]
    public void Predict_AmbiguousBoundaryWithoutPrefix_ReturnsNoSuggestion()
    {
        var result = Predict("hello", TypingLanguage.English);

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Fact]
    public void Predict_MixedLanguageContext_ReturnsNoSuggestion()
    {
        var result = Predict("hello привет ", TypingLanguage.English);

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Theory]
    [InlineData("https://example.com ")]
    [InlineData("C:\\Users\\name ")]
    [InlineData("my_variable ")]
    [InlineData("GitHub ")]
    public void Predict_ProtectedContext_ReturnsNoSuggestion(string context)
    {
        var result = Predict(context, TypingLanguage.English);

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Fact]
    public void Predict_ProtectedPrefix_ReturnsNoSuggestion()
    {
        var result = _service.Predict(new PredictionRequest
        {
            Context = "hello ",
            ActiveLanguage = TypingLanguage.English,
            CurrentWordPrefix = "GitHub",
        });

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Fact]
    public void Predict_ExceedsMaximumSuggestionLength_ReturnsNoSuggestion()
    {
        var service = new PredictionService(new FixedPredictionModel(
        [
            new PredictionModelCandidate { Token = "extraordinarily", FrequencyScore = 0.95 },
        ]));

        var result = service.Predict(new PredictionRequest
        {
            Context = "hello ",
            ActiveLanguage = TypingLanguage.English,
            Options = new PredictionOptions
            {
                MaxSuggestionCharacters = 5,
            },
        });

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Fact]
    public void Predict_LowConfidence_ReturnsLowConfidenceRecommendation()
    {
        var service = new PredictionService(new FixedPredictionModel(
        [
            new PredictionModelCandidate { Token = "maybe", FrequencyScore = 0.40 },
        ]));

        var result = service.Predict(new PredictionRequest
        {
            Context = "hello ",
            ActiveLanguage = TypingLanguage.English,
        });

        Assert.Equal(PredictionRecommendation.LowConfidence, result.Recommendation);
        Assert.Equal("maybe", result.SuggestedContinuation);
        Assert.True(result.Confidence >= PredictionOptions.DefaultLowConfidenceThreshold);
        Assert.True(result.Confidence < PredictionOptions.DefaultSuggestionConfidenceThreshold);
    }

    [Fact]
    public void Predict_BelowLowConfidenceThreshold_ReturnsNoSuggestion()
    {
        var service = new PredictionService(new FixedPredictionModel(
        [
            new PredictionModelCandidate { Token = "maybe", FrequencyScore = 0.20 },
        ]));

        var result = service.Predict(new PredictionRequest
        {
            Context = "hello ",
            ActiveLanguage = TypingLanguage.English,
        });

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
        Assert.Null(result.SuggestedContinuation);
    }

    [Fact]
    public void Predict_NoModelCandidates_ReturnsNoSuggestion()
    {
        var service = new PredictionService(new FixedPredictionModel([]));

        var result = PredictWithService(service, "unknown ", TypingLanguage.English);

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Fact]
    public void Predict_UnsupportedLanguage_ReturnsNoSuggestion()
    {
        var result = _service.Predict(new PredictionRequest
        {
            Context = "hello ",
            ActiveLanguage = (TypingLanguage)999,
        });

        Assert.Equal(PredictionRecommendation.NoSuggestion, result.Recommendation);
    }

    [Fact]
    public void StarterPredictionNgrams_HasDocumentedLimitedSize()
    {
        Assert.InRange(StarterPredictionNgrams.EnglishBigramContextCount, 4, 20);
        Assert.InRange(StarterPredictionNgrams.RussianBigramContextCount, 4, 20);
        Assert.InRange(StarterPredictionNgrams.EnglishTrigramContextCount, 4, 20);
        Assert.InRange(StarterPredictionNgrams.RussianTrigramContextCount, 3, 20);
    }

    private PredictionResult Predict(string context, TypingLanguage language)
    {
        return PredictWithService(_service, context, language);
    }

    private static PredictionResult PredictWithService(
        PredictionService service,
        string context,
        TypingLanguage language)
    {
        return service.Predict(new PredictionRequest
        {
            Context = context,
            ActiveLanguage = language,
        });
    }

    private sealed class FixedPredictionModel(IReadOnlyList<PredictionModelCandidate> candidates) : ILocalPredictionModel
    {
        public IReadOnlyList<PredictionModelCandidate> GetCandidates(
            TypingLanguage language,
            IReadOnlyList<string> contextTokens,
            string? currentWordPrefix)
        {
            IEnumerable<PredictionModelCandidate> filtered = candidates;

            if (!string.IsNullOrWhiteSpace(currentWordPrefix))
            {
                filtered = candidates.Where(candidate =>
                    candidate.Token.StartsWith(currentWordPrefix, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(candidate.Token, currentWordPrefix, StringComparison.OrdinalIgnoreCase));
            }

            return filtered.ToArray();
        }
    }
}
