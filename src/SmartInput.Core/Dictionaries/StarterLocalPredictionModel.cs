using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

/// <summary>
/// Deterministic starter n-gram model backed by <see cref="StarterPredictionNgrams"/>.
/// </summary>
public sealed class StarterLocalPredictionModel : ILocalPredictionModel
{
    public IReadOnlyList<PredictionModelCandidate> GetCandidates(
        TypingLanguage language,
        IReadOnlyList<string> contextTokens,
        string? currentWordPrefix)
    {
        ArgumentNullException.ThrowIfNull(contextTokens);

        if (contextTokens.Count == 0 && string.IsNullOrEmpty(currentWordPrefix))
        {
            return [];
        }

        var normalizedPrefix = NormalizeOptional(currentWordPrefix);
        var normalizedContext = contextTokens
            .Select(AutocorrectDictionaryNormalizer.NormalizeLookupKey)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

        if (normalizedContext.Length >= 2
            && StarterPredictionNgrams.Trigrams.TryGetValue(
                (language, normalizedContext[^2], normalizedContext[^1]),
                out var trigramCandidates))
        {
            return FilterByPrefix(trigramCandidates, normalizedPrefix);
        }

        if (normalizedContext.Length >= 1
            && StarterPredictionNgrams.Bigrams.TryGetValue(
                (language, normalizedContext[^1]),
                out var bigramCandidates))
        {
            return FilterByPrefix(bigramCandidates, normalizedPrefix);
        }

        if (!string.IsNullOrEmpty(normalizedPrefix))
        {
            return [];
        }

        return [];
    }

    private static IReadOnlyList<PredictionModelCandidate> FilterByPrefix(
        IReadOnlyList<(string Token, double Score)> candidates,
        string? normalizedPrefix)
    {
        IEnumerable<(string Token, double Score)> filtered = candidates;

        if (!string.IsNullOrEmpty(normalizedPrefix))
        {
            filtered = candidates.Where(candidate =>
                candidate.Token.StartsWith(normalizedPrefix, StringComparison.Ordinal)
                && !string.Equals(candidate.Token, normalizedPrefix, StringComparison.Ordinal));
        }

        return filtered
            .Select(candidate => new PredictionModelCandidate
            {
                Token = candidate.Token,
                FrequencyScore = candidate.Score,
            })
            .ToArray();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : AutocorrectDictionaryNormalizer.NormalizeLookupKey(value);
    }
}
