using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

internal static class EditOperationClassifier
{
    private static readonly char[] RussianVowels = ['а', 'е', 'ё', 'и', 'о', 'у', 'ы', 'э', 'ю', 'я'];

    internal static EditOperationType Classify(string mutation, string source, TypingLanguage language)
    {
        if (string.IsNullOrEmpty(mutation) || string.IsNullOrEmpty(source))
        {
            return EditOperationType.Unknown;
        }

        if (AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(mutation, source)
            || IsRepeatedCharacterInsertion(mutation, source))
        {
            return EditOperationType.RepeatedAccidentalCharacter;
        }

        if (IsAdjacentTransposition(mutation, source))
        {
            return EditOperationType.AdjacentTransposition;
        }

        if (mutation.Length == source.Length)
        {
            if (IsAdjacentKeySubstitution(mutation, source, language))
            {
                return EditOperationType.AdjacentKeySubstitution;
            }

            if (IsVowelSubstitution(mutation, source, language))
            {
                return EditOperationType.VowelSubstitution;
            }

            // General substitution is exactly one letter change; multi-edit same-length is unknown.
            return IsHammingDistanceOne(mutation, source)
                ? EditOperationType.GeneralSubstitution
                : EditOperationType.Unknown;
        }

        if (mutation.Length == source.Length + 1)
        {
            return IsSingleInsertion(mutation, source)
                ? EditOperationType.ExtraCharacter
                : EditOperationType.Unknown;
        }

        if (mutation.Length == source.Length - 1)
        {
            return IsSingleInsertion(source, mutation)
                ? EditOperationType.MissingCharacter
                : EditOperationType.Unknown;
        }

        return EditOperationType.Unknown;
    }

    private static bool IsHammingDistanceOne(string left, string right)
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

    private static bool IsSingleInsertion(string longer, string shorter)
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

    internal static bool IsAdjacentTransposition(string mutation, string source)
    {
        if (mutation.Length != source.Length || mutation.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < mutation.Length - 1; index++)
        {
            if (mutation[index] == source[index])
            {
                continue;
            }

            if (mutation[index] == source[index + 1]
                && mutation[index + 1] == source[index]
                && string.Equals(
                    mutation[..index] + mutation[(index + 2)..],
                    source[..index] + source[(index + 2)..],
                    StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    internal static bool IsRepeatedCharacterInsertion(string mutation, string source)
    {
        if (mutation.Length != source.Length + 1)
        {
            return false;
        }

        for (var index = 0; index < mutation.Length - 1; index++)
        {
            if (mutation[index] != mutation[index + 1])
            {
                continue;
            }

            if (string.Equals(mutation.Remove(index, 1), source, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAdjacentKeySubstitution(string mutation, string source, TypingLanguage language)
    {
        if (mutation.Length != source.Length)
        {
            return false;
        }

        var differences = 0;
        var diffIndex = -1;
        for (var index = 0; index < mutation.Length; index++)
        {
            if (mutation[index] == source[index])
            {
                continue;
            }

            differences++;
            diffIndex = index;
            if (differences > 1)
            {
                return false;
            }
        }

        if (differences != 1 || diffIndex < 0)
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
        if (mutation.Length != source.Length)
        {
            return false;
        }

        var differences = 0;
        for (var index = 0; index < mutation.Length; index++)
        {
            if (mutation[index] == source[index])
            {
                continue;
            }

            differences++;
            if (differences > 1)
            {
                return false;
            }

            if (language == TypingLanguage.Russian)
            {
                if (!RussianVowels.Contains(mutation[index]) || !RussianVowels.Contains(source[index]))
                {
                    return false;
                }
            }
            else if (!IsEnglishVowel(mutation[index]) || !IsEnglishVowel(source[index]))
            {
                return false;
            }
        }

        return differences == 1;
    }

    private static bool IsEnglishVowel(char character)
    {
        return character is 'a' or 'e' or 'i' or 'o' or 'u'
            or 'A' or 'E' or 'I' or 'O' or 'U';
    }
}
