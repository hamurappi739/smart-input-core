using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

public enum MutationRecoverability
{
    ShouldRecover,
    MustWait,
}

public enum MutationAmbiguityCluster
{
    None,
    KnownTargetCollision,
    MultipleEqualDistanceSources,
    WrongLanguage,
    WrongDirectLayout,
    WrongCombinedLayoutSpelling,
    RepeatedCharacterAmbiguity,
    VowelSubstitutionAmbiguity,
    DeletionAmbiguity,
    InsertionAmbiguity,
    TranspositionAmbiguity,
    CorpusNoise,
    NameAbbreviation,
    RussianMorphology,
    YeYoNormalization,
    CandidateScoringDefect,
    MutationOracleDefect,
}

/// <summary>
/// Labels generated mutations as uniquely recoverable or inherently ambiguous.
/// Used by the corpus audit oracle and mirrors production ambiguity gates.
/// </summary>
public static class MutationRecoverabilityOracle
{
    private const double NearEqualEditCostGap = 0.30;
    private const double ClearFrequencyMargin = 0.15;

    public static MutationRecoverability Classify(
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        CandidateAmbiguityIndex ambiguityIndex,
        out MutationAmbiguityCluster cluster,
        ILayoutConversionService? layoutConverter = null,
        AutocorrectionOptions? options = null)
    {
        options ??= new AutocorrectionOptions();
        cluster = MutationAmbiguityCluster.None;

        if (string.IsNullOrWhiteSpace(mutation)
            || string.IsNullOrWhiteSpace(intendedSource)
            || TrustedWordAnalyzer.IsExactKnownOriginal(mutation, dictionary))
        {
            cluster = MutationAmbiguityCluster.CorpusNoise;
            return MutationRecoverability.MustWait;
        }

        if (!dictionary.Contains(intendedSource, language)
            && !(language == TypingLanguage.Russian
                && RussianYeYoEquivalence.DictionaryContainsWithYeYo(intendedSource, dictionary)))
        {
            cluster = MutationAmbiguityCluster.MutationOracleDefect;
            return MutationRecoverability.MustWait;
        }

        var generated = AutocorrectionCandidateGenerator.Generate(mutation, language, options);
        var known = new List<(string Word, double EditCost, double Frequency)>();
        foreach (var candidate in generated)
        {
            if (!dictionary.Contains(candidate.Word, language))
            {
                continue;
            }

            known.Add((
                candidate.Word,
                candidate.EditCost,
                dictionary.GetFrequency(candidate.Word, language)));
        }

        foreach (var neighbor in ambiguityIndex.FindComparableKnownTargets(mutation, language, 1.0))
        {
            if (known.Any(entry => string.Equals(entry.Word, neighbor.Word, StringComparison.Ordinal)))
            {
                continue;
            }

            known.Add((neighbor.Word, neighbor.EditCost, neighbor.Frequency));
        }

        if (known.Count == 0)
        {
            cluster = MutationAmbiguityCluster.CorpusNoise;
            return MutationRecoverability.MustWait;
        }

        var intended = known.FirstOrDefault(entry =>
            string.Equals(entry.Word, intendedSource, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(intended.Word))
        {
            cluster = MutationAmbiguityCluster.MutationOracleDefect;
            return MutationRecoverability.MustWait;
        }

        var minCost = known.Min(static entry => entry.EditCost);
        if (intended.EditCost > minCost + 0.05)
        {
            cluster = ClassifyEditCluster(mutation, intendedSource);
            return MutationRecoverability.MustWait;
        }

        var near = known
            .Where(entry => entry.EditCost <= minCost + NearEqualEditCostGap)
            .ToList();

        var competitors = near
            .Where(entry => !string.Equals(entry.Word, intendedSource, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (competitors.Count > 0)
        {
            var uniqueRepeated = AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(mutation, intendedSource)
                && competitors.All(entry =>
                    !AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(mutation, entry.Word));
            if (uniqueRepeated)
            {
                cluster = MutationAmbiguityCluster.None;
                return MutationRecoverability.ShouldRecover;
            }

            var strongest = competitors
                .OrderBy(static entry => entry.EditCost)
                .ThenByDescending(static entry => entry.Frequency)
                .First();

            var clearMargin = intended.Frequency >= 0.85
                && intended.Frequency >= strongest.Frequency + ClearFrequencyMargin
                && intended.EditCost <= strongest.EditCost + 0.05;

            if (!clearMargin)
            {
                cluster = competitors.Any(entry =>
                        RussianYeYoEquivalence.FoldYeYo(entry.Word)
                        == RussianYeYoEquivalence.FoldYeYo(intendedSource))
                    ? MutationAmbiguityCluster.YeYoNormalization
                    : ClassifyEditCluster(mutation, intendedSource);
                if (cluster == MutationAmbiguityCluster.None)
                {
                    cluster = MutationAmbiguityCluster.MultipleEqualDistanceSources;
                }

                return MutationRecoverability.MustWait;
            }
        }

        if (layoutConverter is not null)
        {
            var direction = language == TypingLanguage.Russian
                ? LayoutConversionDirection.RussianToEnglish
                : LayoutConversionDirection.EnglishToRussian;
            var mapped = layoutConverter.Convert(mutation, direction);
            if (!string.Equals(mapped, mutation, StringComparison.Ordinal)
                && dictionary.Contains(
                    mapped,
                    language == TypingLanguage.Russian ? TypingLanguage.English : TypingLanguage.Russian))
            {
                var mappedFrequency = dictionary.GetFrequency(
                    mapped,
                    language == TypingLanguage.Russian ? TypingLanguage.English : TypingLanguage.Russian);
                if (mappedFrequency >= intended.Frequency - 0.05)
                {
                    cluster = MutationAmbiguityCluster.WrongDirectLayout;
                    return MutationRecoverability.MustWait;
                }
            }
        }

        cluster = MutationAmbiguityCluster.None;
        return MutationRecoverability.ShouldRecover;
    }

    private static MutationAmbiguityCluster ClassifyEditCluster(string mutation, string source)
    {
        if (AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(mutation, source))
        {
            return MutationAmbiguityCluster.RepeatedCharacterAmbiguity;
        }

        if (mutation.Length == source.Length - 1 || source.Length == mutation.Length - 1)
        {
            return mutation.Length > source.Length
                ? MutationAmbiguityCluster.InsertionAmbiguity
                : MutationAmbiguityCluster.DeletionAmbiguity;
        }

        if (mutation.Length == source.Length)
        {
            var diffs = 0;
            for (var index = 0; index < mutation.Length; index++)
            {
                if (mutation[index] != source[index])
                {
                    diffs++;
                }
            }

            if (diffs == 2)
            {
                return MutationAmbiguityCluster.TranspositionAmbiguity;
            }

            return MutationAmbiguityCluster.VowelSubstitutionAmbiguity;
        }

        return MutationAmbiguityCluster.KnownTargetCollision;
    }
}
