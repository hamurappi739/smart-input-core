using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Reverse edit-signature index over known lexicon words used to detect
/// competing automatic-correction targets before a confident Apply.
/// </summary>
public sealed class CandidateAmbiguityIndex
{
    private const double NearEqualEditCostGap = 0.30;
    private const double ClearFrequencyMargin = 0.15;
    private const double MinClearWinnerFrequency = 0.85;

    private readonly Dictionary<(TypingLanguage Language, string Signature), List<IndexedNeighbor>>? _deletionSignatures;
    private readonly Dictionary<(TypingLanguage Language, string Signature), List<IndexedNeighbor>>? _repeatedCharSignatures;
    private readonly Dictionary<(TypingLanguage Language, string Signature), List<IndexedNeighbor>>? _transpositionSignatures;
    private readonly Dictionary<(TypingLanguage Language, string Signature), List<IndexedNeighbor>>? _insertionSignatures;
    private readonly Dictionary<(TypingLanguage Language, string Signature), List<IndexedNeighbor>>? _substitutionSignatures;
    private readonly Dictionary<(TypingLanguage Language, string Signature), List<IndexedNeighbor>>? _wildcardSubstitutionSignatures;
    private readonly Dictionary<(TypingLanguage Language, string Normalized), List<IndexedNeighbor>>? _yeYoNormalized;
    private readonly Dictionary<(TypingLanguage Language, string LayoutMapped), List<IndexedNeighbor>>? _layoutMapped;
    private readonly HashSet<(TypingLanguage Language, string Word)> _knownWords;
    private readonly Dictionary<(TypingLanguage Language, string Word), double> _frequencies;
    private readonly bool _compact;
    private readonly long _estimatedBytes;

    private CandidateAmbiguityIndex(
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? deletionSignatures,
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? repeatedCharSignatures,
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? transpositionSignatures,
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? insertionSignatures,
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? substitutionSignatures,
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? wildcardSubstitutionSignatures,
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? yeYoNormalized,
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>? layoutMapped,
        HashSet<(TypingLanguage, string)> knownWords,
        Dictionary<(TypingLanguage, string), double>? frequencies = null,
        bool compact = false,
        long estimatedBytes = 0)
    {
        _deletionSignatures = deletionSignatures;
        _repeatedCharSignatures = repeatedCharSignatures;
        _transpositionSignatures = transpositionSignatures;
        _insertionSignatures = insertionSignatures;
        _substitutionSignatures = substitutionSignatures;
        _wildcardSubstitutionSignatures = wildcardSubstitutionSignatures;
        _yeYoNormalized = yeYoNormalized;
        _layoutMapped = layoutMapped;
        _knownWords = knownWords;
        _frequencies = frequencies ?? new Dictionary<(TypingLanguage, string), double>();
        _compact = compact;
        _estimatedBytes = estimatedBytes;
    }

    // Building the full signature map is expensive. ConcurrentDictionary.GetOrAdd
    // may execute its value factory more than once under contention, briefly
    // allocating several hundreds-of-megabytes copies during a parallel startup.
    // A lock makes construction truly single-flight while the finished index stays
    // immutable and lock-free for every lookup.
    private static readonly object CacheGate = new();
    private static CandidateAmbiguityIndex? _starterLexiconCache;

    public static CandidateAmbiguityIndex ForStarterLexicon(ILayoutConversionService? converter = null)
    {
        // Converter only affects layout-mapped signatures; KeyboardLayoutConverter
        // is deterministic, so a second cache entry must not duplicate the maps.
        lock (CacheGate)
        {
            return _starterLexiconCache ??= BuildCompactFromStarterLexicon(
                converter ?? new KeyboardLayoutConverter());
        }
    }

    /// <summary>Test-only: rebuild indexes after lexicon changes within the same process.</summary>
    internal static void ResetCacheForTests()
    {
        lock (CacheGate)
        {
            _starterLexiconCache = null;
        }

        ReverseMutationIndex.ResetCacheForTests();
    }

    public long EstimatedBytes => _estimatedBytes;

    /// <summary>
    /// Builds the production index without materialising a reverse list for
    /// every edit signature. The old eagerly-expanded maps are useful for
    /// offline audits, but cost hundreds of megabytes in the resident app.
    /// </summary>
    private static CandidateAmbiguityIndex BuildCompactFromStarterLexicon(
        ILayoutConversionService converter)
    {
        var known = new HashSet<(TypingLanguage, string)>();
        var frequencies = new Dictionary<(TypingLanguage, string), double>();
        foreach (var pair in StarterAutocorrectLexicon.Entries)
        {
            known.Add(pair.Key);
            frequencies[pair.Key] = pair.Value;
        }

        // The compact representation is roughly a single string table plus
        // frequencies; neighbours are generated only for the current token.
        var estimated = known.Sum(static item => 32L + item.Item2.Length * 2L);
        return new CandidateAmbiguityIndex(
            null, null, null, null, null, null, null, null,
            known, frequencies, compact: true, estimatedBytes: estimated);
    }

    public static CandidateAmbiguityIndex BuildFromStarterLexicon(ILayoutConversionService converter)
    {
        var deletion = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var repeated = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var transposition = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var insertion = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var substitution = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var wildcard = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var yeYo = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var layout = new Dictionary<(TypingLanguage, string), List<IndexedNeighbor>>();
        var known = new HashSet<(TypingLanguage, string)>();

        foreach (var pair in StarterAutocorrectLexicon.Entries)
        {
            var language = pair.Key.Language;
            var word = pair.Key.Word;
            var frequency = pair.Value;
            known.Add((language, word));

            var neighbor = new IndexedNeighbor(word, frequency, 0);

            AddSignature(deletion, language, word, neighbor);
            for (var index = 0; index < word.Length; index++)
            {
                AddSignature(deletion, language, word.Remove(index, 1), new IndexedNeighbor(word, frequency, 1.0));
            }

            if (word.Length is >= 2 and <= 14 && frequency >= 0.70)
            {
                AddRepeatedCharacterSignatures(repeated, language, word, frequency);
            }

            if (word.Length is >= 2 and <= 14)
            {
                AddTranspositionSignatures(transposition, language, word, frequency);
            }

            // Insertion parents are discovered on lookup by deleting one character
            // from the typed token and checking _knownWords (same coverage, no
            // O(L²) forward signature materialization).

            if (word.Length is >= 2 and <= 14 && frequency >= 0.75)
            {
                AddSubstitutionSignatures(substitution, language, word, frequency);
            }

            // Wildcard general-substitution index covers the full lexicon (bounded lookup, not live scan).
            if (word.Length is >= 2 and <= 14)
            {
                AddWildcardSubstitutionSignatures(wildcard, language, word, frequency);
            }

            if (language == TypingLanguage.Russian)
            {
                var folded = RussianYeYoEquivalence.FoldYeYo(word);
                AddSignature(yeYo, language, folded, neighbor);
            }

            if (frequency >= 0.70 && word.Length is >= 2 and <= 14)
            {
                var direction = language == TypingLanguage.Russian
                    ? LayoutConversionDirection.RussianToEnglish
                    : LayoutConversionDirection.EnglishToRussian;
                var mapped = converter.Convert(word, direction);
                if (!string.Equals(mapped, word, StringComparison.OrdinalIgnoreCase)
                    && mapped.Length > 0)
                {
                    AddSignature(layout, language, mapped.ToLowerInvariant(), neighbor);
                }
            }
        }

        var estimatedBytes =
            EstimateMapBytes(deletion)
            + EstimateMapBytes(repeated)
            + EstimateMapBytes(transposition)
            + EstimateMapBytes(insertion)
            + EstimateMapBytes(substitution)
            + EstimateMapBytes(wildcard)
            + EstimateMapBytes(yeYo)
            + EstimateMapBytes(layout)
            + known.Count * 24L;

        return new CandidateAmbiguityIndex(
            deletion,
            repeated,
            transposition,
            insertion,
            substitution,
            wildcard,
            yeYo,
            layout,
            known,
            estimatedBytes: estimatedBytes);
    }

    private static long EstimateMapBytes(Dictionary<(TypingLanguage, string), List<IndexedNeighbor>> map)
    {
        long total = map.Count * 48L;
        foreach (var pair in map)
        {
            total += pair.Key.Item2.Length * 2L;
            total += pair.Value.Count * 32L;
        }

        return total;
    }

    public bool IsKnown(string word, TypingLanguage language)
    {
        return _knownWords.Contains((language, word.ToLowerInvariant()));
    }

    public IReadOnlyList<AmbiguousNeighbor> FindComparableKnownTargets(
        string token,
        TypingLanguage language,
        double bestEditCost)
    {
        if (_compact)
        {
            return FindComparableKnownTargetsCompact(token, language, bestEditCost);
        }

        var normalized = token.ToLowerInvariant();
        var results = new Dictionary<string, AmbiguousNeighbor>(StringComparer.Ordinal);

        void Consider(string word, double frequency, double editCost)
        {
            if (string.Equals(word, normalized, StringComparison.Ordinal))
            {
                return;
            }

            if (editCost > bestEditCost + NearEqualEditCostGap)
            {
                return;
            }

            if (!results.TryGetValue(word, out var existing) || editCost < existing.EditCost)
            {
                results[word] = new AmbiguousNeighbor(word, frequency, editCost);
            }
        }

        if (_deletionSignatures!.TryGetValue((language, normalized), out var exactDeletes))
        {
            foreach (var neighbor in exactDeletes)
            {
                Consider(neighbor.Word, neighbor.Frequency, neighbor.EditCost);
            }
        }

        for (var index = 0; index < normalized.Length; index++)
        {
            var deleted = normalized.Remove(index, 1);
            if (_knownWords.Contains((language, deleted)))
            {
                var frequency = StarterAutocorrectLexicon.Entries.TryGetValue((language, deleted), out var freq)
                    ? freq
                    : 0.0;
                // Mirror AddInsertionSignatures: cheap insertion cost only when the
                // extra character already exists in the parent word and the parent
                // is high-frequency. Otherwise keep the generic delete cost 1.0.
                var cost = frequency >= 0.70
                    && ParentReachableByInternalCharacterInsert(deleted, normalized)
                    ? 0.35
                    : 1.0;
                Consider(deleted, frequency, cost);
            }
        }

        if (_repeatedCharSignatures!.TryGetValue((language, normalized), out var repeatedNeighbors))
        {
            foreach (var neighbor in repeatedNeighbors)
            {
                Consider(neighbor.Word, neighbor.Frequency, 0.35);
            }
        }

        if (_transpositionSignatures!.TryGetValue((language, normalized), out var transpositionNeighbors))
        {
            foreach (var neighbor in transpositionNeighbors)
            {
                Consider(neighbor.Word, neighbor.Frequency, 0.75);
            }
        }

        // Dense insertion signature map was removed; parents are recovered via
        // delete-from-token above (with high-frequency cost 0.35).

        if (_substitutionSignatures!.TryGetValue((language, normalized), out var substitutionNeighbors))
        {
            foreach (var neighbor in substitutionNeighbors)
            {
                Consider(neighbor.Word, neighbor.Frequency, neighbor.EditCost);
            }
        }

        for (var index = 0; index < normalized.Length; index++)
        {
            var wildcardKey = normalized[..index] + "*" + normalized[(index + 1)..];
            if (!_wildcardSubstitutionSignatures!.TryGetValue((language, wildcardKey), out var wildcardNeighbors))
            {
                continue;
            }

            foreach (var neighbor in wildcardNeighbors)
            {
                if (!VerificationEditProvenance.IsHammingDistanceOne(normalized, neighbor.Word))
                {
                    continue;
                }

                var operation = VerificationEditProvenance.Classify(normalized, neighbor.Word, language);
                var cost = VerificationEditProvenance.EstimateEditCost(operation);
                Consider(neighbor.Word, neighbor.Frequency, cost);
            }
        }

        if (language == TypingLanguage.Russian)
        {
            var folded = RussianYeYoEquivalence.FoldYeYo(normalized);
            if (_yeYoNormalized!.TryGetValue((language, folded), out var yeYoNeighbors))
            {
                foreach (var neighbor in yeYoNeighbors)
                {
                    // Membership only — competing e/ё forms create Wait pressure.
                    Consider(neighbor.Word, neighbor.Frequency, 0.05);
                }
            }
        }

        if (_layoutMapped!.TryGetValue((language, normalized), out var layoutNeighbors))
        {
            foreach (var neighbor in layoutNeighbors)
            {
                Consider(neighbor.Word, neighbor.Frequency, 0.50);
            }
        }

        return results.Values
            .OrderBy(static neighbor => neighbor.EditCost)
            .ThenByDescending(static neighbor => neighbor.Frequency)
            .ThenBy(static neighbor => neighbor.Word, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<AmbiguousNeighbor> FindComparableKnownTargetsCompact(
        string token,
        TypingLanguage language,
        double bestEditCost)
    {
        var normalized = token.ToLowerInvariant();
        var results = new Dictionary<string, AmbiguousNeighbor>(StringComparer.Ordinal);
        var alphabet = language == TypingLanguage.Russian
            ? "абвгдеёжзийклмнопрстуфхцчшщъыьэюя"
            : "abcdefghijklmnopqrstuvwxyz";

        void Consider(string candidate, double cost)
        {
            if (candidate.Length == 0
                || string.Equals(candidate, normalized, StringComparison.Ordinal)
                || cost > bestEditCost + NearEqualEditCostGap
                || !_knownWords.Contains((language, candidate)))
            {
                return;
            }

            var key = (language, candidate);
            var frequency = _frequencies.TryGetValue(key, out var value) ? value : 0.0;
            if (!results.TryGetValue(candidate, out var existing) || cost < existing.EditCost)
            {
                results[candidate] = new AmbiguousNeighbor(candidate, frequency, cost);
            }
        }

        // Candidate is shorter (missing-character / repeated-character).
        for (var index = 0; index < normalized.Length; index++)
        {
            var shortened = normalized.Remove(index, 1);
            Consider(shortened, normalized.Length > 1 && normalized[index] == (index > 0 ? normalized[index - 1] : '\0') ? 0.35 : 1.0);
        }

        // Candidate is longer (one inserted character). This replaces the
        // former dense insertion map and is bounded by token length × alphabet.
        for (var index = 0; index <= normalized.Length; index++)
        {
            foreach (var character in alphabet)
            {
                var inflated = normalized.Insert(index, character.ToString());
                Consider(inflated, 1.0);
            }
        }

        // Adjacent transposition and keyboard/vowel substitutions.
        for (var index = 0; index < normalized.Length - 1; index++)
        {
            var chars = normalized.ToCharArray();
            (chars[index], chars[index + 1]) = (chars[index + 1], chars[index]);
            Consider(new string(chars), 0.75);
        }

        for (var index = 0; index < normalized.Length; index++)
        {
            var original = normalized[index];
            foreach (var replacement in alphabet)
            {
                if (replacement == original)
                {
                    continue;
                }

                var candidate = normalized[..index] + replacement + normalized[(index + 1)..];
                var operationCost = KeyboardAdjacencyMap.GetNeighbors(original, language).Contains(replacement)
                    ? 0.65
                    : IsVowel(original, language) && IsVowel(replacement, language) ? 0.80 : 1.0;
                Consider(candidate, operationCost);
            }
        }

        // e/ё is membership-only ambiguity pressure.
        if (language == TypingLanguage.Russian)
        {
            if (normalized.Contains('е'))
            {
                Consider(normalized.Replace('е', 'ё'), 0.05);
            }

            if (normalized.Contains('ё'))
            {
                Consider(normalized.Replace('ё', 'е'), 0.05);
            }
        }

        return results.Values
            .OrderBy(static neighbor => neighbor.EditCost)
            .ThenByDescending(static neighbor => neighbor.Frequency)
            .ThenBy(static neighbor => neighbor.Word, StringComparer.Ordinal)
            .ToList();
    }

    public bool HasAmbiguousCompetition(
        string token,
        TypingLanguage language,
        string proposedTarget,
        double proposedEditCost,
        double proposedFrequency)
    {
        var competitors = FindComparableKnownTargets(token, language, proposedEditCost);
        if (competitors.Count == 0)
        {
            return false;
        }

        var proposedNormalized = proposedTarget.ToLowerInvariant();
        var others = competitors
            .Where(neighbor => !string.Equals(neighbor.Word, proposedNormalized, StringComparison.Ordinal))
            .Where(neighbor => neighbor.EditCost <= proposedEditCost + NearEqualEditCostGap)
            .Where(neighbor =>
            {
                // General-substitution wildcard neighbours only compete inside the tight cost band.
                if (neighbor.EditCost >= 0.95 && proposedEditCost <= 0.85)
                {
                    return neighbor.EditCost <= proposedEditCost + 0.08;
                }

                return true;
            })
            .ToList();

        if (others.Count == 0)
        {
            return false;
        }

        // Repeated-character Apply requires no equal-cost competitor through any other operation.
        if (AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(token, proposedTarget)
            && others.Any(neighbor =>
                !AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(token, neighbor.Word)))
        {
            return true;
        }

        if (AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(token, proposedTarget)
            && others.Any(neighbor =>
                AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(token, neighbor.Word)
                && neighbor.Frequency >= proposedFrequency - 0.05))
        {
            return true;
        }

        if (EditOperationClassifier.IsAdjacentTransposition(token, proposedTarget)
            && others.All(neighbor =>
                !EditOperationClassifier.IsAdjacentTransposition(token, neighbor.Word)
                || neighbor.EditCost > proposedEditCost + 0.05))
        {
            return false;
        }

        var strongestOther = others
            .OrderBy(static neighbor => neighbor.EditCost)
            .ThenByDescending(static neighbor => neighbor.Frequency)
            .First();

        if (proposedEditCost + 0.05 < strongestOther.EditCost
            && proposedFrequency >= MinClearWinnerFrequency
            && proposedFrequency >= strongestOther.Frequency + ClearFrequencyMargin)
        {
            return false;
        }

        if (Math.Abs(proposedEditCost - strongestOther.EditCost) <= 0.05
            && proposedFrequency >= MinClearWinnerFrequency
            && proposedFrequency >= strongestOther.Frequency + ClearFrequencyMargin)
        {
            return false;
        }

        if (proposedFrequency >= 0.995
            && strongestOther.Frequency <= proposedFrequency - 0.004)
        {
            return false;
        }

        return true;
    }

    private static bool ParentReachableByInternalCharacterInsert(string parent, string inflated)
    {
        if (inflated.Length != parent.Length + 1)
        {
            return false;
        }

        for (var index = 0; index < inflated.Length; index++)
        {
            if (!string.Equals(inflated.Remove(index, 1), parent, StringComparison.Ordinal))
            {
                continue;
            }

            return parent.Contains(inflated[index]);
        }

        return false;
    }

    private static void AddRepeatedCharacterSignatures(
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>> map,
        TypingLanguage language,
        string word,
        double frequency)
    {
        for (var index = 0; index < word.Length; index++)
        {
            var inflated = word.Insert(index, word[index].ToString());
            AddSignature(map, language, inflated, new IndexedNeighbor(word, frequency, 0.35));
        }
    }

    private static void AddTranspositionSignatures(
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>> map,
        TypingLanguage language,
        string word,
        double frequency)
    {
        if (word.Length < 2)
        {
            return;
        }

        for (var index = 0; index < word.Length - 1; index++)
        {
            var chars = word.ToCharArray();
            (chars[index], chars[index + 1]) = (chars[index + 1], chars[index]);
            AddSignature(map, language, new string(chars), new IndexedNeighbor(word, frequency, 0.75));
        }
    }

    private static void AddInsertionSignatures(
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>> map,
        TypingLanguage language,
        string word,
        double frequency)
    {
        for (var index = 0; index <= word.Length; index++)
        {
            for (var sourceIndex = 0; sourceIndex < word.Length; sourceIndex++)
            {
                var inflated = word.Insert(index, word[sourceIndex].ToString());
                if (string.Equals(inflated, word, StringComparison.Ordinal))
                {
                    continue;
                }

                AddSignature(map, language, inflated, new IndexedNeighbor(word, frequency, 0.35));
            }
        }
    }

    private static void AddSubstitutionSignatures(
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>> map,
        TypingLanguage language,
        string word,
        double frequency)
    {
        for (var index = 0; index < word.Length; index++)
        {
            var original = word[index];
            foreach (var neighbor in KeyboardAdjacencyMap.GetNeighbors(original, language))
            {
                if (neighbor == original)
                {
                    continue;
                }

                var mutated = word[..index] + neighbor + word[(index + 1)..];
                AddSignature(map, language, mutated, new IndexedNeighbor(word, frequency, 0.65));
            }

            if (IsVowel(original, language))
            {
                foreach (var vowel in GetVowels(language))
                {
                    if (vowel == original)
                    {
                        continue;
                    }

                    var mutated = word[..index] + vowel + word[(index + 1)..];
                    AddSignature(map, language, mutated, new IndexedNeighbor(word, frequency, 0.80));
                }
            }
        }
    }

    private static bool IsVowel(char character, TypingLanguage language)
    {
        if (language == TypingLanguage.Russian)
        {
            return character is 'а' or 'е' or 'ё' or 'и' or 'о' or 'у' or 'ы' or 'э' or 'ю' or 'я';
        }

        return character is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    private static IEnumerable<char> GetVowels(TypingLanguage language)
    {
        return language == TypingLanguage.Russian
            ? ['а', 'е', 'ё', 'и', 'о', 'у', 'ы', 'э', 'ю', 'я']
            : ['a', 'e', 'i', 'o', 'u'];
    }

    internal bool CanDiscoverCompetitor(
        string token,
        TypingLanguage language,
        string competitor,
        double competitorEditCost)
    {
        var normalizedCompetitor = competitor.ToLowerInvariant();
        return FindComparableKnownTargets(token, language, Math.Max(competitorEditCost, 1.0))
            .Any(neighbor => string.Equals(neighbor.Word, normalizedCompetitor, StringComparison.OrdinalIgnoreCase));
    }

    internal readonly record struct SignatureDiscoveryResult(bool Any, IReadOnlyList<ProductionSignatureIndexKind> Indexes);

    internal SignatureDiscoveryResult DiscoverCompetitor(
        string competitor,
        string token,
        TypingLanguage language)
    {
        if (_compact)
        {
            var normalizedCompetitor = competitor.ToLowerInvariant();
            var found = FindComparableKnownTargetsCompact(token, language, 2.0)
                .Any(neighbor => string.Equals(neighbor.Word, normalizedCompetitor, StringComparison.OrdinalIgnoreCase));
            if (!found)
            {
                return new SignatureDiscoveryResult(false, []);
            }

            var operation = EditOperationClassifier.Classify(token.ToLowerInvariant(), normalizedCompetitor, language);
            var kind = operation switch
            {
                EditOperationType.RepeatedAccidentalCharacter => ProductionSignatureIndexKind.RepeatedCharacter,
                EditOperationType.MissingCharacter => ProductionSignatureIndexKind.Deletion,
                EditOperationType.ExtraCharacter => ProductionSignatureIndexKind.Insertion,
                EditOperationType.AdjacentTransposition => ProductionSignatureIndexKind.AdjacentTransposition,
                EditOperationType.AdjacentKeySubstitution => ProductionSignatureIndexKind.AdjacentKeySubstitution,
                EditOperationType.VowelSubstitution => ProductionSignatureIndexKind.VowelSubstitution,
                _ => ProductionSignatureIndexKind.GeneralSubstitution,
            };
            return new SignatureDiscoveryResult(true, [kind]);
        }

        var normalized = token.ToLowerInvariant();
        var hits = new List<ProductionSignatureIndexKind>();

        if (_deletionSignatures!.ContainsKey((language, normalized)))
        {
            hits.Add(ProductionSignatureIndexKind.Deletion);
        }

        if (_repeatedCharSignatures!.ContainsKey((language, normalized)))
        {
            hits.Add(ProductionSignatureIndexKind.RepeatedCharacter);
        }

        if (_transpositionSignatures!.ContainsKey((language, normalized)))
        {
            hits.Add(ProductionSignatureIndexKind.AdjacentTransposition);
        }

        if (_insertionSignatures!.ContainsKey((language, normalized)))
        {
            hits.Add(ProductionSignatureIndexKind.Insertion);
        }

        if (_substitutionSignatures!.ContainsKey((language, normalized)))
        {
            hits.Add(ProductionSignatureIndexKind.AdjacentKeySubstitution);
        }

        if (language == TypingLanguage.Russian)
        {
            var folded = RussianYeYoEquivalence.FoldYeYo(normalized);
            if (_yeYoNormalized!.ContainsKey((language, folded)))
            {
                hits.Add(ProductionSignatureIndexKind.YeYoEquivalence);
            }
        }

        if (_layoutMapped!.ContainsKey((language, normalized)))
        {
            hits.Add(ProductionSignatureIndexKind.DirectPhysicalLayout);
        }

        var viaLookup = FindComparableKnownTargets(token, language, 2.0)
            .Any(neighbor => string.Equals(neighbor.Word, competitor, StringComparison.OrdinalIgnoreCase));
        if (viaLookup && hits.Count == 0)
        {
            hits.Add(ProductionSignatureIndexKind.GeneralSubstitution);
        }

        return new SignatureDiscoveryResult(hits.Count > 0 || viaLookup, hits);
    }

    private static void AddWildcardSubstitutionSignatures(
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>> map,
        TypingLanguage language,
        string word,
        double frequency)
    {
        for (var index = 0; index < word.Length; index++)
        {
            var signature = word[..index] + "*" + word[(index + 1)..];
            AddSignature(map, language, signature, new IndexedNeighbor(word, frequency, 1.0));
        }
    }

    private static void AddSignature(
        Dictionary<(TypingLanguage, string), List<IndexedNeighbor>> map,
        TypingLanguage language,
        string signature,
        IndexedNeighbor neighbor)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return;
        }

        var key = (language, signature);
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        if (list.All(existing => !string.Equals(existing.Word, neighbor.Word, StringComparison.Ordinal)))
        {
            list.Add(neighbor);
        }
    }

    private readonly record struct IndexedNeighbor(string Word, double Frequency, double EditCost);

    public readonly record struct AmbiguousNeighbor(string Word, double Frequency, double EditCost);
}

internal enum ProductionSignatureIndexKind
{
    RepeatedCharacter,
    Deletion,
    Insertion,
    AdjacentTransposition,
    AdjacentKeySubstitution,
    VowelSubstitution,
    GeneralSubstitution,
    YeYoEquivalence,
    DirectPhysicalLayout,
    CombinedLayoutSpelling,
}
