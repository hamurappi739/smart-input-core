using System.Collections.Frozen;

namespace SmartInput.Core.Engines;

internal static class LayoutServiceWordWhitelist
{
    internal const double ConfidenceScore = 0.91;

    private static readonly FrozenDictionary<string, string> EnglishToRussian =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["z"] = "я",
            ["f"] = "а",
            ["d"] = "в",
            ["b"] = "и",
            ["c"] = "с",
            ["r"] = "к",
            ["j"] = "о",
            ["e"] = "у",
            ["ns"] = "ты",
            ["vs"] = "мы",
            ["jy"] = "он",
            ["jyf"] = "она",
            ["yt"] = "не",
            ["yf"] = "на",
            ["gj"] = "по",
            ["pf"] = "за",
            ["jn"] = "от",
            ["lj"] = "до",
            ["bp"] = "из",
            ["'nj"] = "это",
            ["nen"] = "тут",
            ["rfr"] = "как",
            ["jyb"] = "они",
            ["jyj"] = "оно",
            ["xnj"] = "что",
            ["t;br"] = "ежик",
            ["rjn"] = "кот",
            ["xvj"] = "чмо",
            ["jgf"] = "опа",
            ["ldf"] = "два",
            ["nhb"] = "три",
            ["[jnm"] = "хоть",
            ["vtyz"] = "меня",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> RussianToEnglish =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // The only ordinary one-letter English words. Their physical
            // Russian images are rare standalone letters, while the targets
            // are extremely frequent service words/pronouns.
            ["ф"] = "a",
            ["ш"] = "i",
            ["ру"] = "he",
            ["ьу"] = "me",
            ["ьн"] = "my",
            ["црщ"] = "who",
            ["шеы"] = "its",
            ["ше"] = "it",
            ["дуфл"] = "leak",
            ["сфк"] = "car",
            ["рше"] = "hit",
            ["рще"] = "hot",
            ["рщу"] = "hoe",
            ["рун"] = "hey",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal static bool TryGetRussianReplacement(string latinToken, out string russianReplacement)
    {
        if (string.IsNullOrEmpty(latinToken))
        {
            russianReplacement = string.Empty;
            return false;
        }

        if (!IsEnglishLayoutWhitelistToken(latinToken))
        {
            russianReplacement = string.Empty;
            return false;
        }

        // Multi-letter ALL-CAPS input is an abbreviation (VS, US, IT), not a
        // service word typed in the wrong layout. Lower/title-case prose may
        // still use this exact bounded table.
        if (latinToken.Length > 1 && latinToken.All(char.IsUpper))
        {
            russianReplacement = string.Empty;
            return false;
        }

        if (!EnglishToRussian.TryGetValue(latinToken, out russianReplacement!))
        {
            return false;
        }

        if (char.IsUpper(latinToken[0]))
        {
            russianReplacement = char.ToUpperInvariant(russianReplacement[0])
                + russianReplacement[1..];
        }

        return true;
    }

    internal static bool TryGetEnglishReplacement(string russianToken, out string englishReplacement)
    {
        if (string.IsNullOrEmpty(russianToken)
            || russianToken.Any(character => !TokenScriptAnalyzer.IsCyrillicLetter(character)))
        {
            englishReplacement = string.Empty;
            return false;
        }

        if (russianToken.Length > 1 && russianToken.All(char.IsUpper))
        {
            englishReplacement = string.Empty;
            return false;
        }

        if (!RussianToEnglish.TryGetValue(russianToken, out englishReplacement!))
        {
            return false;
        }

        if (char.IsUpper(russianToken[0]))
        {
            englishReplacement = englishReplacement.ToUpperInvariant();
        }

        return true;
    }

    internal static bool TryGetReplacement(
        string token,
        out string replacement,
        out LayoutConversionDirection direction)
    {
        if (TryGetRussianReplacement(token, out replacement))
        {
            direction = LayoutConversionDirection.EnglishToRussian;
            return true;
        }

        if (TryGetEnglishReplacement(token, out replacement))
        {
            direction = LayoutConversionDirection.RussianToEnglish;
            return true;
        }

        replacement = string.Empty;
        direction = default;
        return false;
    }

    /// <summary>
    /// The Russian letter б is the comma key on the English layout.  A leading
    /// comma normally reaches the target as punctuation before the next key is
    /// available, so only this exact two-key service word is reconstructed by
    /// the live boundary gate: ,s → бы.
    /// </summary>
    internal static bool TryGetCommaPrefixedRussianReplacement(
        string token,
        out string russianReplacement)
    {
        if (string.Equals(token, ",s", StringComparison.OrdinalIgnoreCase))
        {
            russianReplacement = "бы";
            return true;
        }

        russianReplacement = string.Empty;
        return false;
    }

    private static bool IsEnglishLayoutWhitelistToken(string token)
        => TokenScriptAnalyzer.ClassifyForLayout(token) == TokenScript.Latin;
}
