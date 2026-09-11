using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Russian е/ё membership helpers. Variants count as evidence of a valid source word,
/// but must never be used to automatically rewrite the user's chosen character.
/// </summary>
internal static class RussianYeYoEquivalence
{
    internal static bool DictionaryContainsWithYeYo(
        string normalizedWord,
        IAutocorrectDictionary dictionary)
    {
        if (dictionary.Contains(normalizedWord, TypingLanguage.Russian))
        {
            return true;
        }

        foreach (var variant in ExpandVariants(normalizedWord))
        {
            if (dictionary.Contains(variant, TypingLanguage.Russian))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool BloomMightContainWithYeYo(string normalizedWord)
    {
        if (RussianWordFormBloomFilter.MightContain(normalizedWord))
        {
            return true;
        }

        foreach (var variant in ExpandVariants(normalizedWord))
        {
            if (RussianWordFormBloomFilter.MightContain(variant))
            {
                return true;
            }
        }

        return false;
    }

    internal static string FoldYeYo(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        var buffer = word.ToCharArray();
        for (var index = 0; index < buffer.Length; index++)
        {
            buffer[index] = buffer[index] switch
            {
                'ё' => 'е',
                'Ё' => 'Е',
                _ => buffer[index],
            };
        }

        return new string(buffer);
    }

    internal static IEnumerable<string> ExpandVariants(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            yield break;
        }

        var hasYe = false;
        var hasYo = false;
        foreach (var character in word)
        {
            if (character is 'е' or 'Е')
            {
                hasYe = true;
            }
            else if (character is 'ё' or 'Ё')
            {
                hasYo = true;
            }
        }

        if (!hasYe && !hasYo)
        {
            yield break;
        }

        if (hasYe)
        {
            yield return ReplaceAll(word, 'е', 'ё', 'Е', 'Ё');
            for (var index = 0; index < word.Length; index++)
            {
                if (word[index] is not ('е' or 'Е'))
                {
                    continue;
                }

                yield return ReplaceAt(word, index, word[index] == 'Е' ? 'Ё' : 'ё');
            }
        }

        if (hasYo)
        {
            yield return ReplaceAll(word, 'ё', 'е', 'Ё', 'Е');
            for (var index = 0; index < word.Length; index++)
            {
                if (word[index] is not ('ё' or 'Ё'))
                {
                    continue;
                }

                yield return ReplaceAt(word, index, word[index] == 'Ё' ? 'Е' : 'е');
            }
        }
    }

    private static string ReplaceAll(string word, char fromLower, char toLower, char fromUpper, char toUpper)
    {
        var buffer = word.ToCharArray();
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] == fromLower)
            {
                buffer[index] = toLower;
            }
            else if (buffer[index] == fromUpper)
            {
                buffer[index] = toUpper;
            }
        }

        return new string(buffer);
    }

    private static string ReplaceAt(string word, int index, char replacement)
    {
        var buffer = word.ToCharArray();
        buffer[index] = replacement;
        return new string(buffer);
    }
}
