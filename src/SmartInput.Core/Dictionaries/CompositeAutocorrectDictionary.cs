using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

public sealed class CompositeAutocorrectDictionary : IAutocorrectDictionary, IDirectFrequencyAutocorrectDictionary
{
    private readonly IUserAutocorrectDictionaryStore _userDictionaryStore;
    private readonly IHunspellWordFormProvider? _wordForms;
    private readonly object _sync = new();

    public CompositeAutocorrectDictionary(
        IUserAutocorrectDictionaryStore userDictionaryStore,
        IHunspellWordFormProvider? wordForms = null)
    {
        _userDictionaryStore = userDictionaryStore;
        _wordForms = wordForms;
    }

    public bool Contains(string word, TypingLanguage language)
    {
        if (ShouldBlockLookup(word))
        {
            return false;
        }

        var lookupKey = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        if (TryGetUserEntry(lookupKey, language, out var userEntry))
        {
            return true;
        }

        return StarterAutocorrectLexicon.Entries.ContainsKey((language, lookupKey))
            || (_wordForms?.IsKnownWord(lookupKey, language) == true);
    }

    public double GetFrequency(string word, TypingLanguage language)
    {
        if (ShouldBlockLookup(word))
        {
            return 0.0;
        }

        var lookupKey = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        if (TryGetUserEntry(lookupKey, language, out var userEntry))
        {
            return userEntry.Frequency ?? AutocorrectDictionaryNormalizer.DefaultUserEntryFrequency;
        }

        if (StarterAutocorrectLexicon.Entries.TryGetValue((language, lookupKey), out var frequency))
        {
            return frequency;
        }

        if (_wordForms?.IsKnownWord(lookupKey, language) != true)
        {
            return 0.0;
        }

        // Hunspell validates morphology but does not provide corpus frequency.
        // Reuse the lemma's corpus rank when possible, so common inflected
        // forms such as "сделал" and "берешь" are not treated as rare words.
        if (_wordForms.TryGetRootWord(lookupKey, language, out var root)
            && StarterAutocorrectLexicon.Entries.TryGetValue(
                (language, AutocorrectDictionaryNormalizer.NormalizeLookupKey(root)),
                out var rootFrequency))
        {
            // A lemma rank is useful evidence but is not the same as a
            // corpus count for every inflected form. Discount it so a rare
            // morphological form (for example an uncommon noun case) cannot
            // outrank a directly attested everyday spelling candidate.
            return Math.Clamp(rootFrequency * 0.82, 0.68, 0.90);
        }

        return 0.68;
    }

    public bool HasDirectFrequency(string word, TypingLanguage language)
    {
        if (ShouldBlockLookup(word))
        {
            return false;
        }

        var lookupKey = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        if (TryGetUserEntry(lookupKey, language, out _))
        {
            return true;
        }

        return StarterAutocorrectLexicon.Entries.ContainsKey((language, lookupKey));
    }

    public bool IsNeverAutocorrect(string word, TypingLanguage language)
    {
        if (ShouldBlockLookup(word))
        {
            return true;
        }

        var lookupKey = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        if (TryGetUserEntry(lookupKey, language, out var userEntry))
        {
            return userEntry.NeverAutocorrect;
        }

        return false;
    }

    public bool IsUserDictionaryEntry(string word, TypingLanguage language)
    {
        if (ShouldBlockLookup(word))
        {
            return false;
        }

        var lookupKey = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
        return TryGetUserEntry(lookupKey, language, out _);
    }

    private bool TryGetUserEntry(
        string lookupKey,
        TypingLanguage language,
        out UserAutocorrectDictionaryEntry entry)
    {
        lock (_sync)
        {
            var match = _userDictionaryStore.Entries.FirstOrDefault(existing =>
                existing.Language == language
                && string.Equals(
                    AutocorrectDictionaryNormalizer.NormalizeLookupKey(existing.Word),
                    lookupKey,
                    StringComparison.Ordinal));

            if (match is null)
            {
                entry = null!;
                return false;
            }

            entry = match;
            return true;
        }
    }

    private static bool ShouldBlockLookup(string word)
    {
        return ProtectedTokenAnalyzer.IsProtected(word);
    }
}
