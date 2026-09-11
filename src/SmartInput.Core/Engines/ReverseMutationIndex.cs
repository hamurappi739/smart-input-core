using System.Collections.Concurrent;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

public readonly record struct PossibleMutationSource(
    string Word,
    double EditCost,
    EditOperationType Operation,
    double Frequency,
    int DictionaryTier,
    bool UsedAdjacentKeys);

public sealed class MutationAnalysisResult
{
    public MutationOracleClass Class { get; init; }
    public string? UniqueTarget { get; init; }
    public EditOperationType Operation { get; init; }
    public IReadOnlyList<PossibleMutationSource> Sources { get; init; } = [];
    public MutationAmbiguityCluster Cluster { get; init; }
}

/// <summary>
/// Maps observed tokens to every lexicon word that could have produced them
/// through a single supported edit operation.
/// </summary>
public sealed class ReverseMutationIndex
{
    private readonly Dictionary<(TypingLanguage Language, string Mutation), List<PossibleMutationSource>> _sources;

    private ReverseMutationIndex(
        Dictionary<(TypingLanguage, string), List<PossibleMutationSource>> sources)
    {
        _sources = sources;
    }

    // Store Lazy values so competing first callers do not build duplicate complete
    // mutation maps before ConcurrentDictionary picks its winning entry.
    private static readonly ConcurrentDictionary<TypingLanguage, Lazy<ReverseMutationIndex>> Cache = new();

    public static ReverseMutationIndex ForLanguage(TypingLanguage language)
    {
        return Cache.GetOrAdd(
            language,
            static requestedLanguage => new Lazy<ReverseMutationIndex>(
                () => BuildForLanguage(requestedLanguage),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>Test-only: drop cached indexes after cost-table or mutation-universe changes.</summary>
    internal static void ResetCacheForTests() => Cache.Clear();

    public IReadOnlyList<PossibleMutationSource> GetSources(string mutation, TypingLanguage language)
    {
        if (string.IsNullOrEmpty(mutation))
        {
            return [];
        }

        var key = (language, mutation.ToLowerInvariant());
        if (!_sources.TryGetValue(key, out var list))
        {
            return [];
        }

        return list;
    }

    private static ReverseMutationIndex BuildForLanguage(TypingLanguage language)
    {
        var map = new Dictionary<(TypingLanguage, string), List<PossibleMutationSource>>();
        var random = new Random(17);

        foreach (var pair in StarterAutocorrectLexicon.Entries.Where(entry => entry.Key.Language == language))
        {
            var word = pair.Key.Word;
            if (word.Length is < 2 or > 14 || !word.All(char.IsLetter))
            {
                continue;
            }

            foreach (var mutation in CreateForwardMutations(word, language, random))
            {
                if (string.Equals(mutation, word, StringComparison.Ordinal))
                {
                    continue;
                }

                var operation = EditOperationClassifier.Classify(mutation, word, language);
                var cost = EstimateEditCost(operation);
                Register(map, language, mutation, word, cost, operation, pair.Value, false);
            }
        }

        return new ReverseMutationIndex(map);
    }

    private static void Register(
        Dictionary<(TypingLanguage, string), List<PossibleMutationSource>> map,
        TypingLanguage language,
        string mutation,
        string source,
        double editCost,
        EditOperationType operation,
        double frequency,
        bool usedAdjacentKeys)
    {
        var key = (language, mutation.ToLowerInvariant());
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        var existingIndex = list.FindIndex(entry =>
            string.Equals(entry.Word, source, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            var existing = list[existingIndex];
            if (editCost < existing.EditCost)
            {
                list[existingIndex] = new PossibleMutationSource(
                    source,
                    editCost,
                    operation,
                    frequency,
                    2,
                    usedAdjacentKeys);
            }

            return;
        }

        list.Add(new PossibleMutationSource(
            source,
            editCost,
            operation,
            frequency,
            2,
            usedAdjacentKeys));
    }

    private static double EstimateEditCost(EditOperationType operation)
        => ProductionEditCostTable.ForOperation(operation);

    private static IEnumerable<string> CreateForwardMutations(
        string word,
        TypingLanguage language,
        Random random)
    {
        _ = random;

        // Exhaustive single-edit mutations so the independent oracle matches brute-force.
        for (var index = 0; index < word.Length; index++)
        {
            yield return word.Insert(index, word[index].ToString());
        }

        for (var index = 0; index < word.Length; index++)
        {
            yield return word.Remove(index, 1);
        }

        for (var index = 0; index < word.Length - 1; index++)
        {
            var buffer = word.ToCharArray();
            (buffer[index], buffer[index + 1]) = (buffer[index + 1], buffer[index]);
            yield return new string(buffer);
        }

        for (var index = 0; index < word.Length; index++)
        {
            var original = word[index];
            foreach (var neighbor in KeyboardAdjacencyMap.GetNeighbors(original, language))
            {
                if (neighbor == original)
                {
                    continue;
                }

                var buffer = word.ToCharArray();
                buffer[index] = neighbor;
                yield return new string(buffer);
            }
        }

        var vowels = language == TypingLanguage.Russian
            ? "аеёиоуыэюя"
            : "aeiou";
        for (var index = 0; index < word.Length; index++)
        {
            if (vowels.IndexOf(word[index]) < 0)
            {
                continue;
            }

            foreach (var vowel in vowels)
            {
                if (vowel == word[index])
                {
                    continue;
                }

                var buffer = word.ToCharArray();
                buffer[index] = vowel;
                yield return new string(buffer);
            }
        }

        // Full-alphabet substitutions: align production reverse index with the
        // verification one-edit universe (general / adjacent / vowel provenance).
        var alphabet = VerificationEditProvenance.GetAlphabet(language);
        for (var index = 0; index < word.Length; index++)
        {
            var original = word[index];
            foreach (var replacement in alphabet)
            {
                if (replacement == original)
                {
                    continue;
                }

                var buffer = word.ToCharArray();
                buffer[index] = replacement;
                yield return new string(buffer);
            }
        }

        // Extra character: complete language alphabet (same universe as verification).
        // Word length is already gated in BuildForLanguage (<=14), so this stays bounded.
        for (var index = 0; index <= word.Length; index++)
        {
            foreach (var insertion in alphabet)
            {
                yield return word.Insert(index, insertion.ToString());
            }
        }
    }
}

/// <summary>
/// Classifies observed tokens using reverse mutation evidence and generator candidates.
/// </summary>
public static class MutationClassificationOracle
{
    private const double NearEqualEditGap = 0.08;
    private const double ExtendedHighFrequencyBand = 0.20;
    private const double ClearFrequencyMargin = 0.12;

    public static MutationAnalysisResult Analyze(
        string token,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null)
    {
        options ??= new AutocorrectionOptions();

        if (string.IsNullOrWhiteSpace(token) || !token.All(char.IsLetter))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.InvalidMutation,
                Cluster = MutationAmbiguityCluster.CorpusNoise,
            };
        }

        if (ProtectedTokenAnalyzer.IsProtected(token))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.ProtectedMutation,
                Cluster = MutationAmbiguityCluster.CorpusNoise,
            };
        }

        if (!IsLanguageScriptCompatible(token, language))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.CrossLanguageCollision,
                Cluster = MutationAmbiguityCluster.WrongLanguage,
            };
        }

        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.MutationIsExactKnownWord,
                Cluster = MutationAmbiguityCluster.CorpusNoise,
            };
        }

        var normalized = token.ToLowerInvariant();
        var sources = CollectSources(normalized, language, dictionary, options);
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

        var credibleApplyTargets = ResolveCredibleApplyTargets(applyBand, out var cluster);
        if (credibleApplyTargets.Count != 1)
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.Ambiguous,
                Sources = sources,
                Cluster = cluster,
            };
        }

        var uniqueWinner = credibleApplyTargets[0];
        if (!IsDominantApplyTarget(uniqueWinner, applyBand))
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
            UniqueTarget = uniqueWinner.Word,
            Operation = uniqueWinner.Operation,
            Sources = sources,
            Cluster = MutationAmbiguityCluster.None,
        };
    }

    /// <summary>
    /// Classify an already-collected source list with the same apply-band rules as <see cref="Analyze"/>.
    /// Used by the exhaustive brute-force verifier.
    /// </summary>
    public static MutationAnalysisResult ClassifyFromSources(IReadOnlyList<PossibleMutationSource> sources)
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
        var credibleApplyTargets = ResolveCredibleApplyTargets(applyBand, out var cluster);
        if (credibleApplyTargets.Count != 1)
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.Ambiguous,
                Sources = sources,
                Cluster = cluster,
            };
        }

        var uniqueWinner = credibleApplyTargets[0];
        if (!IsDominantApplyTarget(uniqueWinner, applyBand))
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
            UniqueTarget = uniqueWinner.Word,
            Operation = uniqueWinner.Operation,
            Sources = sources,
            Cluster = MutationAmbiguityCluster.None,
        };
    }

    public static MutationAnalysisResult AnalyzeGeneratedCase(
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null)
    {
        var analysis = Analyze(mutation, language, dictionary, options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return analysis;
        }

        // Generated-case accounting: Unique≠intended is Ambiguous so WrongUniqueTarget
        // is not conflated with a genuine Unique recovery. Production Apply still uses
        // HasDiscardedCredibleCompetitor (credible near-cost → Wait) on live evidence.
        if (!string.Equals(analysis.UniqueTarget, intendedSource, StringComparison.OrdinalIgnoreCase))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.Ambiguous,
                UniqueTarget = analysis.UniqueTarget,
                Operation = analysis.Operation,
                Sources = analysis.Sources,
                Cluster = MutationAmbiguityCluster.MultipleEqualDistanceSources,
            };
        }

        return analysis;
    }

    private static List<PossibleMutationSource> CollectSources(
        string normalized,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options)
    {
        _ = options;
        // Exhaustive one-edit neighbourhood — order-independent and aligned with the
        // verification universe. Live path stays O(len × alphabet) dictionary probes.
        return ProductionOneEditCompetitorUniverse.Collect(normalized, language, dictionary);
    }

    private static PossibleMutationSource? SelectUniqueWinner(
        string normalized,
        IReadOnlyList<PossibleMutationSource> minimumBand,
        out MutationAmbiguityCluster cluster)
    {
        cluster = MutationAmbiguityCluster.None;
        if (minimumBand.Count == 0)
        {
            cluster = MutationAmbiguityCluster.CorpusNoise;
            return null;
        }

        if (minimumBand.Count == 1)
        {
            return minimumBand[0];
        }

        var operation = minimumBand[0].Operation;
        var sameOperation = minimumBand
            .Where(source => source.Operation == operation)
            .ToList();

        if (operation == EditOperationType.RepeatedAccidentalCharacter)
        {
            var repeatedOnly = sameOperation
                .Where(source => AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(normalized, source.Word))
                .ToList();
            if (repeatedOnly.Count == 1)
            {
                return repeatedOnly[0];
            }

            cluster = MutationAmbiguityCluster.RepeatedCharacterAmbiguity;
            return null;
        }

        if (operation == EditOperationType.AdjacentTransposition)
        {
            var transpositions = minimumBand
                .Where(source => EditOperationClassifier.IsAdjacentTransposition(normalized, source.Word))
                .OrderByDescending(static source => source.Frequency)
                .ThenBy(static source => source.EditCost)
                .ThenBy(static source => source.Word, StringComparer.Ordinal)
                .ToList();
            if (transpositions.Count == 1)
            {
                return transpositions[0];
            }

            if (transpositions.Count >= 2
                && HasClearWinner(transpositions[0], transpositions[1], operation))
            {
                return transpositions[0];
            }

            cluster = MutationAmbiguityCluster.TranspositionAmbiguity;
            return null;
        }

        if (minimumBand.Any(static source => source.Operation == EditOperationType.AdjacentKeySubstitution))
        {
            var frequencyChampion = minimumBand[0];
            var minCost = minimumBand.Min(static source => source.EditCost);
            var adjacentAtMin = minimumBand
                .Where(source => source.Operation == EditOperationType.AdjacentKeySubstitution)
                .Where(source => source.EditCost <= minCost + 0.01)
                .OrderByDescending(static source => source.Frequency)
                .ThenBy(static source => source.EditCost)
                .ThenBy(static source => source.Word, StringComparer.Ordinal)
                .ToList();
            if (adjacentAtMin.Count >= 2
                && !HasClearWinner(adjacentAtMin[0], adjacentAtMin[1], EditOperationType.AdjacentKeySubstitution))
            {
                cluster = MutationAmbiguityCluster.KnownTargetCollision;
                return null;
            }

            if (adjacentAtMin.Count == 1)
            {
                var adjacentWinner = adjacentAtMin[0];
                if (frequencyChampion.EditCost <= adjacentWinner.EditCost + NearEqualEditGap
                    || HasClearWinner(frequencyChampion, adjacentWinner, frequencyChampion.Operation))
                {
                    // High-frequency band winner beats keyboard-neighbour min-cost shortcut.
                }
                else
                {
                    return adjacentWinner;
                }
            }
        }

        var champion = minimumBand[0];
        var challenger = minimumBand[1];
        if (HasClearWinner(champion, challenger, operation))
        {
            return champion;
        }

        cluster = operation switch
        {
            EditOperationType.MissingCharacter => MutationAmbiguityCluster.DeletionAmbiguity,
            EditOperationType.ExtraCharacter => MutationAmbiguityCluster.InsertionAmbiguity,
            EditOperationType.VowelSubstitution => MutationAmbiguityCluster.VowelSubstitutionAmbiguity,
            EditOperationType.AdjacentKeySubstitution => MutationAmbiguityCluster.KnownTargetCollision,
            _ => MutationAmbiguityCluster.MultipleEqualDistanceSources,
        };
        return null;
    }

    private static bool HasClearWinner(
        PossibleMutationSource champion,
        PossibleMutationSource challenger,
        EditOperationType operation)
    {
        // Ultra-high-frequency insert/delete beats cheaper adjacent-key near-collisions
        // (e.g. helo→hello over help/hell). Documented apply-band rule, not a cost change.
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

    private static bool IsDominantApplyTarget(
        PossibleMutationSource winner,
        IReadOnlyList<PossibleMutationSource> applyBand)
    {
        return applyBand.All(source =>
            string.Equals(source.Word, winner.Word, StringComparison.Ordinal)
            || !SourcesWithinCredibleBand(source, winner)
            || HasClearWinner(winner, source, ResolveComparisonOperation(winner, source)));
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
            .GroupBy(static source => source.Word, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(static source => source.Frequency)
                .ThenBy(static source => source.EditCost)
                .ThenBy(static source => source.DictionaryTier)
                .First())
            .ToList();

        var survivors = distinctTargets
            .Where(candidate => !distinctTargets.Any(other =>
                !string.Equals(other.Word, candidate.Word, StringComparison.Ordinal)
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
                    string.Equals(other.Word, frequencyChampion.Word, StringComparison.Ordinal)
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

    private static bool SourcesWithinCredibleBand(
        PossibleMutationSource left,
        PossibleMutationSource right)
    {
        var costGap = left.Operation switch
        {
            EditOperationType.AdjacentKeySubstitution => 0.01,
            _ => 0.08,
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

    private static List<PossibleMutationSource> BuildApplyBand(
        IReadOnlyList<PossibleMutationSource> sources,
        double minimumCost)
    {
        var merged = new Dictionary<string, PossibleMutationSource>(StringComparer.Ordinal);

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

        foreach (var source in BuildCredibleBand(sources, minimumCost))
        {
            Consider(source);
        }

        foreach (var source in BuildMinimumBand(sources, minimumCost))
        {
            Consider(source);
        }

        return merged.Values
            .OrderByDescending(static source => source.Frequency)
            .ThenBy(static source => source.EditCost)
            .ThenBy(static source => source.Word, StringComparer.Ordinal)
            .ToList();
    }

    private static List<PossibleMutationSource> BuildCredibleBand(
        IReadOnlyList<PossibleMutationSource> sources,
        double minimumCost)
    {
        const double extendedBand = 0.35;

        return sources
            .Where(source => source.EditCost <= minimumCost + extendedBand)
            .Where(source => source.Frequency >= 0.50 || source.DictionaryTier >= 1)
            .GroupBy(static source => source.Word, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(static source => source.EditCost)
                .ThenByDescending(static source => source.Frequency)
                .First())
            .OrderByDescending(static source => source.Frequency)
            .ThenBy(static source => source.EditCost)
            .ThenBy(static source => source.Word, StringComparer.Ordinal)
            .ToList();
    }

    private static List<PossibleMutationSource> BuildMinimumBand(
        IReadOnlyList<PossibleMutationSource> sources,
        double minimumCost)
    {
        var band = sources
            .Where(source => source.EditCost <= minimumCost + NearEqualEditGap)
            .ToList();

        foreach (var source in sources)
        {
            if (source.Frequency < 0.995
                || source.EditCost > minimumCost + ExtendedHighFrequencyBand)
            {
                continue;
            }

            if (band.Any(entry => string.Equals(entry.Word, source.Word, StringComparison.Ordinal)))
            {
                continue;
            }

            band.Add(source);
        }

        return band
            .OrderByDescending(static source => source.Frequency)
            .ThenBy(static source => source.EditCost)
            .ThenBy(static source => source.Word, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsLanguageScriptCompatible(string token, TypingLanguage language)
    {
        return language switch
        {
            TypingLanguage.English => TokenScriptAnalyzer.Classify(token) == TokenScript.Latin,
            TypingLanguage.Russian => TokenScriptAnalyzer.Classify(token) == TokenScript.Cyrillic,
            _ => false,
        };
    }
}
