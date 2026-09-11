using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Canonical edit provenance for verification.
/// When multiple operations explain the same pair, the strongest wins.
///
/// Order rationale:
/// 1. RepeatedAccidentalCharacter — accidental double-press is the most specific length±1 signal.
/// 2. AdjacentTransposition — exact swap of two neighbours is more specific than substitution.
/// 3. AdjacentKeySubstitution — physical keyboard adjacency before generic letter swaps.
/// 4. VowelSubstitution — linguistic vowel class before arbitrary one-letter swaps.
/// 5. MissingCharacter / ExtraCharacter — single insert/delete after specialised forms.
/// 6. GeneralSubstitution — residual same-length Hamming-1 only.
/// </summary>
public static class VerificationEditProvenance
{
    public const string RussianAlphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
    public const string EnglishAlphabet = "abcdefghijklmnopqrstuvwxyz";

    public static string GetAlphabet(TypingLanguage language)
        => language == TypingLanguage.Russian ? RussianAlphabet : EnglishAlphabet;

    public static EditOperationType Classify(string observed, string source, TypingLanguage language)
    {
        if (string.IsNullOrEmpty(observed) || string.IsNullOrEmpty(source))
        {
            return EditOperationType.Unknown;
        }

        var mutation = observed.ToLowerInvariant();
        var target = source.ToLowerInvariant();

        if (AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(mutation, target)
            || EditOperationClassifier.IsRepeatedCharacterInsertion(mutation, target))
        {
            return EditOperationType.RepeatedAccidentalCharacter;
        }

        if (EditOperationClassifier.IsAdjacentTransposition(mutation, target))
        {
            return EditOperationType.AdjacentTransposition;
        }

        if (mutation.Length == target.Length)
        {
            if (!IsHammingDistanceOne(mutation, target))
            {
                return EditOperationType.Unknown;
            }

            if (IsAdjacentKeySubstitution(mutation, target, language))
            {
                return EditOperationType.AdjacentKeySubstitution;
            }

            if (IsVowelSubstitution(mutation, target, language))
            {
                return EditOperationType.VowelSubstitution;
            }

            return EditOperationType.GeneralSubstitution;
        }

        if (mutation.Length == target.Length + 1 && IsSingleCharacterInsertion(mutation, target))
        {
            return EditOperationType.ExtraCharacter;
        }

        if (mutation.Length == target.Length - 1 && IsSingleCharacterInsertion(target, mutation))
        {
            return EditOperationType.MissingCharacter;
        }

        return EditOperationType.Unknown;
    }

    public static double EstimateEditCost(EditOperationType operation)
        => ProductionEditCostTable.ForOperation(operation);

    public static bool IsHammingDistanceOne(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var differences = 0;
        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] == right[index])
            {
                continue;
            }

            differences++;
            if (differences > 1)
            {
                return false;
            }
        }

        return differences == 1;
    }

    public static bool IsSingleCharacterInsertion(string longer, string shorter)
    {
        if (longer.Length != shorter.Length + 1)
        {
            return false;
        }

        for (var index = 0; index < longer.Length; index++)
        {
            if (string.Equals(longer.Remove(index, 1), shorter, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAdjacentKeySubstitution(string mutation, string source, TypingLanguage language)
    {
        var diffIndex = -1;
        for (var index = 0; index < mutation.Length; index++)
        {
            if (mutation[index] == source[index])
            {
                continue;
            }

            diffIndex = index;
            break;
        }

        if (diffIndex < 0)
        {
            return false;
        }

        var left = mutation[diffIndex];
        var right = source[diffIndex];
        return KeyboardAdjacencyMap.GetNeighbors(left, language).Contains(right)
            || KeyboardAdjacencyMap.GetNeighbors(right, language).Contains(left);
    }

    private static bool IsVowelSubstitution(string mutation, string source, TypingLanguage language)
    {
        var diffIndex = -1;
        for (var index = 0; index < mutation.Length; index++)
        {
            if (mutation[index] == source[index])
            {
                continue;
            }

            diffIndex = index;
            break;
        }

        if (diffIndex < 0)
        {
            return false;
        }

        return language == TypingLanguage.Russian
            ? IsRussianVowel(mutation[diffIndex]) && IsRussianVowel(source[diffIndex])
            : IsEnglishVowel(mutation[diffIndex]) && IsEnglishVowel(source[diffIndex]);
    }

    private static bool IsRussianVowel(char character)
        => character is 'а' or 'е' or 'ё' or 'и' or 'о' or 'у' or 'ы' or 'э' or 'ю' or 'я';

    private static bool IsEnglishVowel(char character)
        => character is 'a' or 'e' or 'i' or 'o' or 'u';
}
