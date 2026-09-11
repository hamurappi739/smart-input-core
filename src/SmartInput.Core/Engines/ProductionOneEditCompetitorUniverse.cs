using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Production one-edit competitor universe for mutation classification.
/// Enumerates the same edit neighbourhood as the verification universe
/// (full alphabet insert/delete/substitute + transposition), filtered by the
/// live dictionary. Bounded by token length — safe for the Apply path.
/// Must not call test-only oracles.
/// </summary>
public static class ProductionOneEditCompetitorUniverse
{
    private const int MaxTokenLength = 20;

    public static List<PossibleMutationSource> Collect(
        string observedToken,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        var normalized = observedToken.ToLowerInvariant();
        if (normalized.Length is 0 or > MaxTokenLength)
        {
            return [];
        }

        var merged = new Dictionary<string, PossibleMutationSource>(StringComparer.OrdinalIgnoreCase);

        void Consider(string word)
        {
            if (!dictionary.Contains(word, language)
                || string.Equals(word, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var operation = VerificationEditProvenance.Classify(normalized, word, language);
            if (operation == EditOperationType.Unknown)
            {
                return;
            }

            var cost = ProductionEditCostTable.ForOperation(operation);
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

        for (var index = 0; index <= normalized.Length; index++)
        {
            foreach (var insertion in alphabet)
            {
                Consider(normalized.Insert(index, insertion.ToString()));
            }
        }

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
