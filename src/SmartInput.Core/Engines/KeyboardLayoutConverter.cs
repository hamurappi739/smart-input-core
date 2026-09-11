using System.Collections.Frozen;

namespace SmartInput.Core.Engines;

public sealed class KeyboardLayoutConverter : ILayoutConversionService
{
    private static readonly FrozenDictionary<char, char> EnglishToRussian =
        KeyboardLayoutMaps.BuildEnglishToRussianMap();

    private static readonly FrozenDictionary<char, char> RussianToEnglish =
        KeyboardLayoutMaps.BuildRussianToEnglishMap();

    public string Convert(string input, LayoutConversionDirection direction)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var map = direction == LayoutConversionDirection.EnglishToRussian
            ? EnglishToRussian
            : RussianToEnglish;

        return ConvertUsingMap(input, map);
    }

    internal static string ConvertUsingMap(string input, IReadOnlyDictionary<char, char> map)
    {
        var buffer = new char[input.Length];

        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            buffer[index] = map.TryGetValue(character, out var converted)
                ? converted
                : character;
        }

        return new string(buffer);
    }
}

internal static class KeyboardLayoutMaps
{
    internal static FrozenDictionary<char, char> BuildEnglishToRussianMap()
    {
        var pairs = new Dictionary<char, char>();

        AddParallelPairs(pairs, "`", "ё");
        AddParallelPairs(pairs, "~", "Ё");
        AddParallelPairs(pairs, "qwertyuiop[]", "йцукенгшщзхъ");
        AddParallelPairs(pairs, "asdfghjkl;'", "фывапролджэ");
        AddParallelPairs(pairs, "zxcvbnm,./", "ячсмитьбю.");
        AddParallelPairs(pairs, "\\", "/");

        AddParallelPairs(pairs, "~", "Ё");
        AddParallelPairs(pairs, "@", "\"");
        AddParallelPairs(pairs, "#", "№");
        AddParallelPairs(pairs, "$", ";");
        AddParallelPairs(pairs, "^", ":");
        AddParallelPairs(pairs, "&", "?");
        AddParallelPairs(pairs, ":", "Ж");
        AddParallelPairs(pairs, "\"", "Э");
        AddParallelPairs(pairs, "<", "Б");
        AddParallelPairs(pairs, ">", "Ю");
        AddParallelPairs(pairs, "?", ",");

        return pairs.ToFrozenDictionary();
    }

    internal static FrozenDictionary<char, char> BuildRussianToEnglishMap()
    {
        var englishToRussian = BuildEnglishToRussianMap();
        var reverse = new Dictionary<char, char>(englishToRussian.Count);

        foreach (var pair in englishToRussian)
        {
            reverse[pair.Value] = pair.Key;
        }

        return reverse.ToFrozenDictionary();
    }

    private static void AddParallelPairs(IDictionary<char, char> pairs, string english, string russian)
    {
        if (english.Length != russian.Length)
        {
            throw new InvalidOperationException("Layout map strings must have equal length.");
        }

        for (var index = 0; index < english.Length; index++)
        {
            var englishCharacter = english[index];
            var russianCharacter = russian[index];
            pairs[englishCharacter] = russianCharacter;

            if (char.IsLetter(englishCharacter))
            {
                pairs[char.ToUpperInvariant(englishCharacter)] = char.ToUpperInvariant(russianCharacter);
            }
        }
    }
}
