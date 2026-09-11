using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

internal static class PredictionCandidateRanker
{
    internal sealed record RankedPredictionCandidate(
        string Token,
        double Confidence,
        bool RequiresLeadingSpace);

    internal static RankedPredictionCandidate? RankBest(
        IReadOnlyList<PredictionModelCandidate> candidates,
        PredictionContextSnapshot snapshot,
        TypingLanguage activeLanguage,
        PredictionOptions options)
    {
        RankedPredictionCandidate? best = null;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Token))
            {
                continue;
            }

            if (ProtectedTokenAnalyzer.IsProtected(candidate.Token))
            {
                continue;
            }

            if (!IsLanguageCompatible(candidate.Token, activeLanguage))
            {
                continue;
            }

            if (!IsPunctuationCompatible(candidate.Token, snapshot))
            {
                continue;
            }

            var confidence = ComputeConfidence(candidate, snapshot, activeLanguage, options);
            var ranked = new RankedPredictionCandidate(
                candidate.Token,
                confidence,
                RequiresLeadingSpace: false);

            if (best is null || IsBetterCandidate(ranked, best))
            {
                best = ranked;
            }
        }

        return best;
    }

    private static bool IsBetterCandidate(
        RankedPredictionCandidate candidate,
        RankedPredictionCandidate currentBest)
    {
        if (candidate.Confidence > currentBest.Confidence)
        {
            return true;
        }

        if (candidate.Confidence < currentBest.Confidence)
        {
            return false;
        }

        return string.Compare(candidate.Token, currentBest.Token, StringComparison.Ordinal) < 0;
    }

    private static double ComputeConfidence(
        PredictionModelCandidate candidate,
        PredictionContextSnapshot snapshot,
        TypingLanguage activeLanguage,
        PredictionOptions options)
    {
        var confidence = candidate.FrequencyScore;

        if (!string.IsNullOrEmpty(snapshot.CurrentWordPrefix)
            && candidate.Token.StartsWith(snapshot.CurrentWordPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var prefixLength = snapshot.CurrentWordPrefix.Length;
            var matchRatio = prefixLength / (double)Math.Max(candidate.Token.Length, 1);
            confidence += 0.08 + (matchRatio * 0.10);
        }

        if (IsLanguageCompatible(candidate.Token, activeLanguage))
        {
            confidence += 0.03;
        }

        if (snapshot.FollowsSentenceEndingPunctuation && char.IsUpper(candidate.Token[0]))
        {
            confidence += 0.04;
        }

        if (snapshot.FollowsClausePunctuation && char.IsLower(candidate.Token[0]))
        {
            confidence += 0.02;
        }

        if (snapshot.BoundaryKind == PredictionBoundaryKind.MidWord
            && !string.IsNullOrEmpty(snapshot.CurrentWordPrefix)
            && candidate.Token.Length > snapshot.CurrentWordPrefix.Length)
        {
            confidence += 0.02;
        }

        return Math.Clamp(confidence, 0.0, 1.0);
    }

    private static bool IsLanguageCompatible(string token, TypingLanguage activeLanguage)
    {
        var resolved = Services.TypingLanguageResolver.Resolve(token);
        return resolved == activeLanguage;
    }

    private static bool IsPunctuationCompatible(string token, PredictionContextSnapshot snapshot)
    {
        if (snapshot.FollowsSentenceEndingPunctuation)
        {
            return char.IsUpper(token[0]);
        }

        if (snapshot.FollowsClausePunctuation)
        {
            return char.IsLower(token[0]);
        }

        return true;
    }
}
