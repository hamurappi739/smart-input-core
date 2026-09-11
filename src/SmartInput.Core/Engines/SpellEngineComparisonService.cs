using System.Diagnostics;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Compares two local spelling engines while retaining aggregate counters only.
/// Input tokens are never stored in the returned summary.
/// </summary>
public sealed record SpellEngineComparisonCase(
    string Token,
    TypingLanguage Language);

public sealed record SpellEngineComparisonSummary(
    int TotalCases,
    int CurrentCandidates,
    int ExternalCandidates,
    int CurrentWaits,
    int ExternalWaits,
    int RecommendationDisagreements,
    int ExactWordsChecked,
    int ExactWordsChangedByExternal,
    int ProtectedTokensChecked,
    int ProtectedTokensChangedByExternal,
    double ExternalAverageMilliseconds,
    double ExternalMaxMilliseconds);

public interface ISpellEngineComparisonService
{
    SpellEngineComparisonSummary Compare(
        IEnumerable<SpellEngineComparisonCase> cases,
        IAutocorrectionService currentService,
        IExternalAutocorrectionEvaluator externalEvaluator,
        IExternalSpellCorrectionProvider externalProvider,
        IAutocorrectDictionary dictionary);
}

public sealed class SpellEngineComparisonService : ISpellEngineComparisonService
{
    public SpellEngineComparisonSummary Compare(
        IEnumerable<SpellEngineComparisonCase> cases,
        IAutocorrectionService currentService,
        IExternalAutocorrectionEvaluator externalEvaluator,
        IExternalSpellCorrectionProvider externalProvider,
        IAutocorrectDictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(currentService);
        ArgumentNullException.ThrowIfNull(externalEvaluator);
        ArgumentNullException.ThrowIfNull(externalProvider);
        ArgumentNullException.ThrowIfNull(dictionary);

        var total = 0;
        var currentCandidates = 0;
        var externalCandidates = 0;
        var currentWaits = 0;
        var externalWaits = 0;
        var disagreements = 0;
        var exactChecked = 0;
        var exactChanged = 0;
        var protectedChecked = 0;
        var protectedChanged = 0;
        double externalTotalMilliseconds = 0.0;
        double externalMaxMilliseconds = 0.0;

        foreach (var item in cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Token))
            {
                continue;
            }

            total++;
            var current = currentService.Evaluate(item.Token, item.Language, dictionary);

            var stopwatch = Stopwatch.StartNew();
            var external = externalEvaluator.Evaluate(
                item.Token,
                item.Language,
                dictionary,
                externalProvider,
                new Configuration.AutocorrectionOptions { UseExternalProvider = true });
            stopwatch.Stop();

            var elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            externalTotalMilliseconds += elapsedMilliseconds;
            externalMaxMilliseconds = Math.Max(externalMaxMilliseconds, elapsedMilliseconds);

            currentCandidates += current.Recommendation == AutocorrectionRecommendation.Candidate ? 1 : 0;
            externalCandidates += external.Recommendation == AutocorrectionRecommendation.Candidate ? 1 : 0;
            currentWaits += current.Recommendation == AutocorrectionRecommendation.Wait ? 1 : 0;
            externalWaits += external.Recommendation == AutocorrectionRecommendation.Wait ? 1 : 0;
            if (current.Recommendation != external.Recommendation
                || !string.Equals(current.CandidateToken, external.CandidateToken, StringComparison.Ordinal))
            {
                disagreements++;
            }

            var isProtected = ProtectedTokenAnalyzer.IsProtected(item.Token);
            var isExact = dictionary.Contains(item.Token, item.Language);
            if (isProtected)
            {
                protectedChecked++;
                if (external.Recommendation == AutocorrectionRecommendation.Candidate)
                {
                    protectedChanged++;
                }
            }

            if (isExact)
            {
                exactChecked++;
                if (external.Recommendation == AutocorrectionRecommendation.Candidate)
                {
                    exactChanged++;
                }
            }
        }

        return new SpellEngineComparisonSummary(
            total,
            currentCandidates,
            externalCandidates,
            currentWaits,
            externalWaits,
            disagreements,
            exactChecked,
            exactChanged,
            protectedChecked,
            protectedChanged,
            total == 0 ? 0.0 : externalTotalMilliseconds / total,
            externalMaxMilliseconds);
    }
}
