using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Verifier-only classification path. Duplicates the production apply-band specification
/// without calling <see cref="MutationClassificationOracle.ClassifyFromSources"/> or any
/// production Apply gate. Spec shared; code independent.
/// </summary>
internal static class IndependentVerifierClassifier
{
    private const double NearEqualEditGap = 0.08;
    private const double ExtendedHighFrequencyBand = 0.20;
    private const double CredibleExtendedBand = 0.35;
    private const double ClearFrequencyMargin = 0.12;

    internal static MutationAnalysisResult ClassifyFromSources(IReadOnlyList<PossibleMutationSource> sources)
    {
        if (sources.Count == 0)
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.Ambiguous,
                Sources = sources,
                Cluster = MutationAmbiguityCluster.CorpusNoise,
            };
        }

        var minimumCost = sources.Min(static source => source.EditCost);
        var applyBand = BuildApplyBand(sources, minimumCost);
        var survivors = ResolveCredibleApplyTargets(applyBand, out var cluster);
        if (survivors.Count != 1)
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.Ambiguous,
                Sources = sources,
                Cluster = cluster,
            };
        }

        var winner = survivors[0];
        if (!IsDominantApplyTarget(winner, applyBand))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.Ambiguous,
                Sources = sources,
                Cluster = MutationAmbiguityCluster.MultipleEqualDistanceSources,
            };
        }

        return new MutationAnalysisResult
        {
            Class = MutationOracleClass.UniquelyRecoverable,
            UniqueTarget = winner.Word,
            Operation = winner.Operation,
            Sources = sources,
            Cluster = MutationAmbiguityCluster.None,
        };
    }

    private static List<PossibleMutationSource> BuildApplyBand(
        IReadOnlyList<PossibleMutationSource> sources,
        double minimumCost)
    {
        var merged = new Dictionary<string, PossibleMutationSource>(StringComparer.OrdinalIgnoreCase);

        void Consider(PossibleMutationSource source)
        {
            if (merged.TryGetValue(source.Word, out var existing))
            {
                if (source.Frequency > existing.Frequency
                    || (Math.Abs(source.Frequency - existing.Frequency) < 0.0001
                        && source.EditCost < existing.EditCost))
                {
                    merged[source.Word] = source;
                }

                return;
            }

            merged[source.Word] = source;
        }

        foreach (var source in sources.Where(entry =>
                     entry.EditCost <= minimumCost + CredibleExtendedBand
                     && (entry.Frequency >= 0.50 || entry.DictionaryTier >= 1)))
        {
            Consider(source);
        }

        foreach (var source in sources.Where(entry => entry.EditCost <= minimumCost + NearEqualEditGap))
        {
            Consider(source);
        }

        foreach (var source in sources.Where(entry =>
                     entry.Frequency >= 0.995
                     && entry.EditCost <= minimumCost + ExtendedHighFrequencyBand))
        {
            Consider(source);
        }

        return merged.Values
            .OrderByDescending(static source => source.Frequency)
            .ThenBy(static source => source.EditCost)
            .ThenBy(static source => source.Word, StringComparer.Ordinal)
            .ToList();
    }

    private static List<PossibleMutationSource> ResolveCredibleApplyTargets(
        IReadOnlyList<PossibleMutationSource> credibleBand,
        out MutationAmbiguityCluster cluster)
    {
        cluster = MutationAmbiguityCluster.None;
        if (credibleBand.Count == 0)
        {
            cluster = MutationAmbiguityCluster.CorpusNoise;
            return [];
        }

        var distinctTargets = credibleBand
            .GroupBy(static source => source.Word, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(static source => source.Frequency)
                .ThenBy(static source => source.EditCost)
                .ThenBy(static source => source.DictionaryTier)
                .First())
            .ToList();

        var survivors = distinctTargets
            .Where(candidate => !distinctTargets.Any(other =>
                !string.Equals(other.Word, candidate.Word, StringComparison.OrdinalIgnoreCase)
                && SourcesWithinCredibleBand(other, candidate)
                && HasClearWinner(other, candidate, ResolveComparisonOperation(other, candidate))))
            .ToList();

        if (survivors.Count == 0)
        {
            cluster = MutationAmbiguityCluster.MultipleEqualDistanceSources;
            return survivors;
        }

        if (survivors.Count > 1)
        {
            var frequencyChampion = survivors
                .OrderByDescending(static source => source.Frequency)
                .ThenBy(static source => source.EditCost)
                .ThenBy(static source => source.Word, StringComparer.Ordinal)
                .First();

            if (frequencyChampion.Frequency >= 0.995
                && survivors.All(other =>
                    string.Equals(other.Word, frequencyChampion.Word, StringComparison.OrdinalIgnoreCase)
                    || HasClearWinner(
                        frequencyChampion,
                        other,
                        ResolveComparisonOperation(frequencyChampion, other))))
            {
                return [frequencyChampion];
            }

            cluster = MutationAmbiguityCluster.MultipleEqualDistanceSources;
        }

        return survivors;
    }

    private static bool IsDominantApplyTarget(
        PossibleMutationSource winner,
        IReadOnlyList<PossibleMutationSource> applyBand)
        => applyBand.All(source =>
            string.Equals(source.Word, winner.Word, StringComparison.OrdinalIgnoreCase)
            || !SourcesWithinCredibleBand(source, winner)
            || HasClearWinner(winner, source, ResolveComparisonOperation(winner, source)));

    private static EditOperationType ResolveComparisonOperation(
        PossibleMutationSource left,
        PossibleMutationSource right)
    {
        if (left.Operation == right.Operation)
        {
            return left.Operation;
        }

        if (left.EditCost + 0.01 < right.EditCost)
        {
            return left.Operation;
        }

        if (right.EditCost + 0.01 < left.EditCost)
        {
            return right.Operation;
        }

        return left.Frequency >= right.Frequency ? left.Operation : right.Operation;
    }

    private static bool SourcesWithinCredibleBand(PossibleMutationSource left, PossibleMutationSource right)
    {
        var costGap = left.Operation switch
        {
            EditOperationType.AdjacentKeySubstitution => 0.01,
            _ => NearEqualEditGap,
        };

        if (left.EditCost > right.EditCost + costGap
            && right.EditCost > left.EditCost + costGap)
        {
            return false;
        }

        var margin = left.Operation switch
        {
            EditOperationType.AdjacentKeySubstitution => 0.12,
            EditOperationType.VowelSubstitution or EditOperationType.GeneralSubstitution => 0.12,
            _ => 0.10,
        };

        return left.Frequency >= right.Frequency - margin
            || right.Frequency >= left.Frequency - margin;
    }

    private static bool HasClearWinner(
        PossibleMutationSource champion,
        PossibleMutationSource challenger,
        EditOperationType operation)
    {
        if (champion.Operation is EditOperationType.MissingCharacter or EditOperationType.ExtraCharacter
            && champion.Frequency >= 0.999
            && challenger.Operation == EditOperationType.AdjacentKeySubstitution
            && champion.Frequency + 0.0001 >= challenger.Frequency)
        {
            return true;
        }

        if (challenger.Operation is EditOperationType.MissingCharacter or EditOperationType.ExtraCharacter
            && challenger.Frequency >= 0.999
            && champion.Operation == EditOperationType.AdjacentKeySubstitution
            && challenger.Frequency + 0.0001 >= champion.Frequency)
        {
            return false;
        }

        if (champion.EditCost + 0.01 < challenger.EditCost)
        {
            if (challenger.Frequency >= 0.995
                && challenger.Frequency >= champion.Frequency + 0.004)
            {
                return false;
            }

            return true;
        }

        if (champion.EditCost > challenger.EditCost + NearEqualEditGap)
        {
            if (champion.Frequency >= 0.995
                && champion.Frequency >= challenger.Frequency + 0.004)
            {
                return true;
            }

            return false;
        }

        if (operation is EditOperationType.VowelSubstitution or EditOperationType.GeneralSubstitution
            or EditOperationType.AdjacentKeySubstitution)
        {
            if (champion.Frequency >= 0.995 && challenger.Frequency <= champion.Frequency - 0.004)
            {
                return true;
            }

            return champion.Frequency >= 0.90
                && champion.Frequency >= challenger.Frequency + ClearFrequencyMargin;
        }

        return champion.Frequency >= challenger.Frequency + ClearFrequencyMargin
            || champion.Frequency >= 0.995;
    }
}
