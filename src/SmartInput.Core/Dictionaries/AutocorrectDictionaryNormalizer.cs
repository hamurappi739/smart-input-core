using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

public static class AutocorrectDictionaryNormalizer
{
    public const double DefaultUserEntryFrequency = 0.75;
    public const int MaxUserDictionaryEntries = 8_192;
    public const int MaxUserDictionaryWordLength = 256;

    public static string NormalizeLookupKey(string word)
    {
        return word.Trim().ToLowerInvariant();
    }

    public static bool TryNormalizeEntryWord(string? word, out string normalizedWord)
    {
        normalizedWord = word?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalizedWord)
            && normalizedWord.Length <= MaxUserDictionaryWordLength
            && normalizedWord.All(static character => !char.IsControl(character));
    }

    public static bool TryNormalizeEntry(
        UserAutocorrectDictionaryEntry? entry,
        out UserAutocorrectDictionaryEntry normalizedEntry)
    {
        normalizedEntry = null!;
        if (entry is null
            || !Enum.IsDefined(entry.Language)
            || !TryNormalizeEntryWord(entry.Word, out var normalizedWord)
            || (entry.Frequency is double frequency
                && (!double.IsFinite(frequency) || frequency < 0.0 || frequency > 1.0)))
        {
            return false;
        }

        normalizedEntry = new UserAutocorrectDictionaryEntry
        {
            Word = normalizedWord,
            Language = entry.Language,
            Frequency = entry.Frequency,
            NeverAutocorrect = entry.NeverAutocorrect,
        };
        return true;
    }

    public static (TypingLanguage Language, string LookupKey) CreateLookupKey(
        string word,
        TypingLanguage language)
    {
        return (language, NormalizeLookupKey(word));
    }
}
