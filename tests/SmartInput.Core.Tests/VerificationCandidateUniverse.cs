using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Test-only exhaustive candidate universe shared by brute-force and the independent oracle.
/// Generates every single-edit neighbour of the observed token against the language alphabet,
/// then keeps dictionary hits. Must never be wired into the live typing path.
/// </summary>
internal static class VerificationCandidateUniverse
{
    internal static List<PossibleMutationSource> Collect(
        string observedToken,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        var normalized = observedToken.ToLowerInvariant();
        var merged = new Dictionary<string, PossibleMutationSource>(StringComparer.OrdinalIgnoreCase);

        void Consider(string word)
        {
            if (!StarterAutocorrectLexicon.Entries.ContainsKey((language, word))
                || !dictionary.Contains(word, language)
                || string.Equals(word, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var operation = VerificationEditProvenance.Classify(normalized, word, language);
            if (operation == EditOperationType.Unknown)
            {
                return;
            }

            var cost = VerifierEditCostTable.ForOperation(operation);
            var frequency = dictionary.GetFrequency(word, language);
            var candidate = new PossibleMutationSource(
                word,
                cost,
                operation,
                frequency,
                AutocorrectionCandidateRanker.GetDictionaryTier(word, language, dictionary),
                operation == EditOperationType.AdjacentKeySubstitution);

            if (!merged.TryGetValue(word, out var existing) || IsStronger(candidate, existing))
            {
                merged[word] = candidate;
            }
        }

        var alphabet = VerificationEditProvenance.GetAlphabet(language);

        // Exhaustive same-length one-character substitutions (general / vowel / adjacent-key).
        for (var index = 0; index < normalized.Length; index++)
        {
            foreach (var replacement in alphabet)
            {
                if (replacement == normalized[index])
                {
                    continue;
                }

                Consider(normalized[..index] + replacement + normalized[(index + 1)..]);
            }

            Consider(normalized.Remove(index, 1));
        }

        // Exhaustive single-character insertions.
        for (var index = 0; index <= normalized.Length; index++)
        {
            foreach (var insertion in alphabet)
            {
                Consider(normalized.Insert(index, insertion.ToString()));
            }
        }

        // Adjacent transpositions.
        for (var index = 0; index < normalized.Length - 1; index++)
        {
            var chars = normalized.ToCharArray();
            (chars[index], chars[index + 1]) = (chars[index + 1], chars[index]);
            Consider(new string(chars));
        }

        if (language == TypingLanguage.Russian)
        {
            var folded = RussianYeYoEquivalence.FoldYeYo(normalized);
            if (!string.Equals(folded, normalized, StringComparison.Ordinal))
            {
                Consider(folded);
            }
        }

        return merged.Values
            .OrderBy(static source => source.EditCost)
            .ThenByDescending(static source => source.Frequency)
            .ThenBy(static source => source.Word, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsStronger(PossibleMutationSource candidate, PossibleMutationSource existing)
    {
        if (candidate.EditCost < existing.EditCost - 0.001)
        {
            return true;
        }

        if (Math.Abs(candidate.EditCost - existing.EditCost) > 0.001)
        {
            return false;
        }

        var candidateRank = ProvenanceRank(candidate.Operation);
        var existingRank = ProvenanceRank(existing.Operation);
        if (candidateRank != existingRank)
        {
            return candidateRank < existingRank;
        }

        return candidate.Frequency > existing.Frequency;
    }

    private static int ProvenanceRank(EditOperationType operation)
        => operation switch
        {
            EditOperationType.RepeatedAccidentalCharacter => 0,
            EditOperationType.AdjacentTransposition => 1,
            EditOperationType.AdjacentKeySubstitution => 2,
            EditOperationType.VowelSubstitution => 3,
            EditOperationType.MissingCharacter => 4,
            EditOperationType.ExtraCharacter => 5,
            EditOperationType.GeneralSubstitution => 6,
            _ => 99,
        };
}
