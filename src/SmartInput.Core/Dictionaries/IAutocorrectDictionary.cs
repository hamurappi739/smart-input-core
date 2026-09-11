using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

public interface IAutocorrectDictionary
{
    bool Contains(string word, TypingLanguage language);

    double GetFrequency(string word, TypingLanguage language);

    bool IsNeverAutocorrect(string word, TypingLanguage language);

    bool IsUserDictionaryEntry(string word, TypingLanguage language);
}

/// <summary>
/// Optional capability for dictionaries that can distinguish a directly
/// corpus-backed entry from a morphology-only word form. Exact corpus words
/// are trusted; morphology-only forms may still be sent through the
/// conservative spelling probe when their spelling is suspicious.
/// </summary>
public interface IDirectFrequencyAutocorrectDictionary
{
    bool HasDirectFrequency(string word, TypingLanguage language);
}
