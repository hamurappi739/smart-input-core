using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

public interface IPredictionService
{
    PredictionResult Predict(PredictionRequest request);
}

public sealed class PredictionService : IPredictionService
{
    private readonly ILocalPredictionModel _predictionModel;

    public PredictionService(ILocalPredictionModel predictionModel)
    {
        _predictionModel = predictionModel;
    }

    public PredictionResult Predict(PredictionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = request.Options ?? new PredictionOptions();
        var context = request.Context ?? string.Empty;

        if (!IsSupportedLanguage(request.ActiveLanguage))
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        if (context.Length < options.MinContextCharacters
            && string.IsNullOrWhiteSpace(request.CurrentWordPrefix))
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        var snapshot = PredictionContextAnalyzer.Analyze(
            context,
            request.CurrentWordPrefix,
            request.ActiveLanguage);

        if (snapshot.HasProtectedToken || snapshot.HasMixedLanguageTokens)
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        if (snapshot.BoundaryKind == PredictionBoundaryKind.Ambiguous)
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        if (snapshot.CompletedTokens.Count < options.MinContextWords
            && snapshot.BoundaryKind == PredictionBoundaryKind.WordStart)
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        var boundedContext = snapshot.CompletedTokens.Count <= options.MaxContextWords
            ? snapshot.CompletedTokens
            : snapshot.CompletedTokens
                .Skip(snapshot.CompletedTokens.Count - options.MaxContextWords)
                .ToArray();

        var candidates = _predictionModel.GetCandidates(
            request.ActiveLanguage,
            boundedContext,
            snapshot.CurrentWordPrefix);

        if (candidates.Count == 0)
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        var best = PredictionCandidateRanker.RankBest(
            candidates,
            snapshot,
            request.ActiveLanguage,
            options);

        if (best is null)
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        var continuation = BuildContinuation(best, snapshot);
        if (string.IsNullOrEmpty(continuation))
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        if (continuation.Length > options.MaxSuggestionCharacters)
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        var tokenCount = CountTokens(continuation, snapshot);
        if (tokenCount <= 0 || tokenCount > options.MaxSuggestionTokens)
        {
            return PredictionResult.NoSuggestion(context, request.ActiveLanguage);
        }

        var recommendation = ResolveRecommendation(best.Confidence, options);

        return new PredictionResult
        {
            Context = context,
            SuggestedContinuation = recommendation == PredictionRecommendation.NoSuggestion
                ? null
                : continuation,
            Confidence = best.Confidence,
            TokenCount = recommendation == PredictionRecommendation.NoSuggestion ? 0 : tokenCount,
            Recommendation = recommendation,
            ActiveLanguage = request.ActiveLanguage,
        };
    }

    private static bool IsSupportedLanguage(TypingLanguage activeLanguage)
    {
        return activeLanguage is TypingLanguage.English or TypingLanguage.Russian;
    }

    private static string BuildContinuation(
        PredictionCandidateRanker.RankedPredictionCandidate candidate,
        PredictionContextSnapshot snapshot)
    {
        if (!string.IsNullOrEmpty(snapshot.CurrentWordPrefix))
        {
            if (!candidate.Token.StartsWith(snapshot.CurrentWordPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return candidate.Token[snapshot.CurrentWordPrefix.Length..];
        }

        if (snapshot.BoundaryKind == PredictionBoundaryKind.WordStart)
        {
            return NeedsLeadingSpace(snapshot) ? " " + candidate.Token : candidate.Token;
        }

        return candidate.Token;
    }

    private static bool NeedsLeadingSpace(PredictionContextSnapshot snapshot)
    {
        if (snapshot.EndsWithWhitespace)
        {
            return false;
        }

        if (snapshot.FollowsSentenceEndingPunctuation || snapshot.FollowsClausePunctuation)
        {
            return true;
        }

        return false;
    }

    private static int CountTokens(string continuation, PredictionContextSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(snapshot.CurrentWordPrefix))
        {
            var trimmed = continuation.TrimStart();
            return string.IsNullOrWhiteSpace(trimmed) ? 0 : trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        return 1;
    }

    private static PredictionRecommendation ResolveRecommendation(double confidence, PredictionOptions options)
    {
        if (confidence >= options.SuggestionConfidenceThreshold)
        {
            return PredictionRecommendation.Suggestion;
        }

        if (confidence >= options.LowConfidenceThreshold)
        {
            return PredictionRecommendation.LowConfidence;
        }

        return PredictionRecommendation.NoSuggestion;
    }
}
