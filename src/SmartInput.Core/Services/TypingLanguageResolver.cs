using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

internal static class TypingLanguageResolver
{
    internal static TypingLanguage? Resolve(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var lettersOnly = new string(token.Where(char.IsLetter).ToArray());
        if (string.IsNullOrEmpty(lettersOnly))
        {
            return null;
        }

        return TokenScriptAnalyzer.Classify(lettersOnly) switch
        {
            TokenScript.Latin => TypingLanguage.English,
            TokenScript.Cyrillic => TypingLanguage.Russian,
            _ => null,
        };
    }
}
