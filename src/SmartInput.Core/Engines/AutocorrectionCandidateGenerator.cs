using SmartInput.Core.Configuration;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

internal readonly struct GeneratedAutocorrectionCandidate
{
    public GeneratedAutocorrectionCandidate(string word, double editCost, bool usedAdjacentKeys)
    {
        Word = word;
        EditCost = editCost;
        UsedAdjacentKeys = usedAdjacentKeys;
    }

    public string Word { get; }

    public double EditCost { get; }

    public bool UsedAdjacentKeys { get; }
}

/// <summary>
/// Bounded candidate generation. All operation families are enumerated fully
/// (order-independent), then the cheapest unique candidates are selected.
/// Early truncation during enumeration is forbidden because it makes the
/// candidate set depend on generation order.
/// </summary>
internal static class AutocorrectionCandidateGenerator
{
    // Soft cap during enumeration only — final selection still respects MaxGeneratedCandidates.
    private const int EnumerationSoftCap = 4096;

    internal static IReadOnlyList<GeneratedAutocorrectionCandidate> Generate(
        string token,
        TypingLanguage language,
        AutocorrectionOptions options)
    {
        var normalized = token.ToLowerInvariant();
        var collector = new CandidateCollector(normalized, EnumerationSoftCap);

        AddTranspositions(normalized, collector);
        AddRepeatedCharacterCleanup(normalized, collector);
        AddDeletions(normalized, collector);
        AddAdjacentSubstitutions(normalized, language, collector);
        AddLanguageSpecificSubstitutions(normalized, language, collector, options);
        AddInsertions(normalized, language, collector);

        return collector.Values
            .OrderBy(static candidate => candidate.EditCost)
            .ThenBy(static candidate => candidate.Word, StringComparer.Ordinal)
            .Take(options.MaxGeneratedCandidates)
            .ToList();
    }

    private static void AddDeletions(string token, CandidateCollector collector)
    {
        for (var index = 0; index < token.Length; index++)
        {
            collector.TryAdd(token.Remove(index, 1), ProductionEditCostTable.MissingCharacter, false);
        }
    }

    private static void AddInsertions(string token, TypingLanguage language, CandidateCollector collector)
    {
        for (var index = 0; index <= token.Length; index++)
        {
            var left = index > 0 ? token[index - 1] : (char?)null;
            var right = index < token.Length ? token[index] : (char?)null;

            if (left is char leftCharacter)
            {
                collector.TryAdd(
                    token.Insert(index, leftCharacter.ToString()),
                    ProductionEditCostTable.RepeatedAccidentalCharacter,
                    false);
            }

            if (right is char rightCharacter && rightCharacter != left)
            {
                collector.TryAdd(
                    token.Insert(index, rightCharacter.ToString()),
                    ProductionEditCostTable.RepeatedAccidentalCharacter,
                    false);
            }

            if (left is char leftNeighborSource)
            {
                foreach (var neighbor in KeyboardAdjacencyMap.GetNeighbors(leftNeighborSource, language))
                {
                    collector.TryAdd(
                        token.Insert(index, neighbor.ToString()),
                        ProductionEditCostTable.ExtraCharacter,
                        true);
                }
            }

            if (right is char rightNeighborSource)
            {
                foreach (var neighbor in KeyboardAdjacencyMap.GetNeighbors(rightNeighborSource, language))
                {
                    collector.TryAdd(
                        token.Insert(index, neighbor.ToString()),
                        ProductionEditCostTable.ExtraCharacter,
                        true);
                }
            }

            foreach (var vowel in GetVowelInsertions(left, right, language))
            {
                collector.TryAdd(
                    token.Insert(index, vowel.ToString()),
                    ProductionEditCostTable.VowelInsertion,
                    false);
            }
        }
    }

    private static IEnumerable<char> GetVowelInsertions(char? left, char? right, TypingLanguage language)
    {
        if (left is char leftCharacter && right is char rightCharacter
            && IsConsonant(leftCharacter, language) && IsConsonant(rightCharacter, language))
        {
            return KeyboardAdjacencyMap.GetCommonVowels(language);
        }

        return [];
    }

    private static bool IsConsonant(char character, TypingLanguage language)
    {
        var vowels = KeyboardAdjacencyMap.GetCommonVowels(language);
        return !vowels.Contains(char.ToLowerInvariant(character));
    }

    private static void AddAdjacentSubstitutions(
        string token,
        TypingLanguage language,
        CandidateCollector collector)
    {
        var buffer = token.ToCharArray();

        for (var index = 0; index < buffer.Length; index++)
        {
            foreach (var neighbor in KeyboardAdjacencyMap.GetNeighbors(buffer[index], language))
            {
                buffer[index] = neighbor;
                collector.TryAdd(new string(buffer), ProductionEditCostTable.AdjacentKeySubstitution, true);
                buffer[index] = token[index];
            }
        }
    }

    private static void AddLanguageSpecificSubstitutions(
        string token,
        TypingLanguage language,
        CandidateCollector collector,
        AutocorrectionOptions options)
    {
        if (language == TypingLanguage.Russian)
        {
            AddRussianVowelConfusions(token, collector);
        }

        if (token.Length <= options.MaxSubstitutionTokenLength)
        {
            AddSubstitutions(token, language, collector);
        }
    }

    private static void AddRussianVowelConfusions(string token, CandidateCollector collector)
    {
        var pairs = new (char Left, char Right)[]
        {
            ('е', 'и'),
            ('и', 'е'),
            ('а', 'я'),
            ('я', 'а'),
            ('о', 'а'),
            ('а', 'о'),
            ('ы', 'и'),
            ('и', 'ы'),
            ('у', 'ю'),
            ('ю', 'у'),
            ('е', 'ё'),
            ('ё', 'е'),
        };

        var buffer = token.ToCharArray();
        for (var index = 0; index < buffer.Length; index++)
        {
            foreach (var (left, right) in pairs)
            {
                if (buffer[index] != left)
                {
                    continue;
                }

                buffer[index] = right;
                collector.TryAdd(new string(buffer), ProductionEditCostTable.VowelSubstitution, false);
                buffer[index] = left;
            }
        }
    }

    private static void AddSubstitutions(
        string token,
        TypingLanguage language,
        CandidateCollector collector)
    {
        var buffer = token.ToCharArray();
        var alphabet = GetAlphabet(language);

        for (var index = 0; index < buffer.Length; index++)
        {
            var original = buffer[index];

            foreach (var replacement in alphabet)
            {
                if (replacement == original)
                {
                    continue;
                }

                buffer[index] = replacement;
                collector.TryAdd(new string(buffer), ProductionEditCostTable.GeneralSubstitution, false);
                buffer[index] = original;
            }
        }
    }

    private static IEnumerable<char> GetAlphabet(TypingLanguage language)
        => VerificationEditProvenance.GetAlphabet(language);

    private static void AddTranspositions(string token, CandidateCollector collector)
    {
        if (token.Length < 2)
        {
            return;
        }

        var buffer = token.ToCharArray();

        for (var index = 0; index < token.Length - 1; index++)
        {
            (buffer[index], buffer[index + 1]) = (buffer[index + 1], buffer[index]);
            collector.TryAdd(new string(buffer), ProductionEditCostTable.AdjacentTransposition, false);
            (buffer[index], buffer[index + 1]) = (buffer[index + 1], buffer[index]);
        }
    }

    private static void AddRepeatedCharacterCleanup(string token, CandidateCollector collector)
    {
        for (var index = 0; index < token.Length - 1; index++)
        {
            if (token[index] != token[index + 1])
            {
                continue;
            }

            collector.TryAdd(
                token.Remove(index, 1),
                ProductionEditCostTable.RepeatedAccidentalCharacter,
                false);
        }
    }

    private sealed class CandidateCollector
    {
        private readonly Dictionary<string, GeneratedAutocorrectionCandidate> _candidates;
        private readonly string _normalized;
        private readonly int _softCap;

        internal CandidateCollector(string normalized, int softCap)
        {
            _normalized = normalized;
            _softCap = softCap;
            _candidates = new Dictionary<string, GeneratedAutocorrectionCandidate>(StringComparer.Ordinal);
        }

        internal IEnumerable<GeneratedAutocorrectionCandidate> Values => _candidates.Values;

        internal void TryAdd(string candidate, double editCost, bool usedAdjacentKeys)
        {
            if (string.Equals(candidate, _normalized, StringComparison.Ordinal)
                || candidate.Length == 0)
            {
                return;
            }

            if (_candidates.TryGetValue(candidate, out var existing))
            {
                if (existing.EditCost <= editCost)
                {
                    return;
                }

                _candidates[candidate] = new GeneratedAutocorrectionCandidate(
                    candidate,
                    editCost,
                    usedAdjacentKeys);
                return;
            }

            if (_candidates.Count >= _softCap)
            {
                // Soft cap: only accept if strictly cheaper than the current worst retained candidate.
                var worst = _candidates.Values
                    .OrderByDescending(static entry => entry.EditCost)
                    .ThenByDescending(static entry => entry.Word, StringComparer.Ordinal)
                    .First();
                if (editCost > worst.EditCost
                    || (Math.Abs(editCost - worst.EditCost) < 0.001
                        && string.CompareOrdinal(candidate, worst.Word) >= 0))
                {
                    return;
                }

                _candidates.Remove(worst.Word);
            }

            _candidates[candidate] = new GeneratedAutocorrectionCandidate(
                candidate,
                editCost,
                usedAdjacentKeys);
        }
    }
}
