namespace SmartInput.Core.Engines;

internal enum TokenScript
{
    Empty,
    Latin,
    Cyrillic,
    Mixed,
    Other,
}

internal static class TokenScriptAnalyzer
{
    internal static TokenScript Classify(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return TokenScript.Empty;
        }

        var hasLatin = false;
        var hasCyrillic = false;
        var hasOther = false;

        foreach (var character in token)
        {
            if (IsLatinLetter(character))
            {
                hasLatin = true;
                continue;
            }

            if (IsCyrillicLetter(character))
            {
                hasCyrillic = true;
                continue;
            }

            hasOther = true;
        }

        if (hasOther)
        {
            return TokenScript.Other;
        }

        if (hasLatin && hasCyrillic)
        {
            return TokenScript.Mixed;
        }

        if (hasLatin)
        {
            return TokenScript.Latin;
        }

        if (hasCyrillic)
        {
            return TokenScript.Cyrillic;
        }

        return TokenScript.Other;
    }

    /// <summary>
    /// Classifies a token for layout conversion while allowing punctuation
    /// keys that produce Russian letters under the English layout. Examples:
    /// <c>'nj</c> -&gt; <c>это</c>, <c>t;br</c> -&gt; <c>ежик</c> and
    /// <c>[jnm</c> -&gt; <c>хоть</c>. The ordinary classifier intentionally keeps
    /// punctuation as <see cref="TokenScript.Other"/> for spelling logic.
    /// </summary>
    internal static TokenScript ClassifyForLayout(string token)
    {
        var script = Classify(token);
        if (script != TokenScript.Other || string.IsNullOrEmpty(token))
        {
            return script;
        }

        var hasLatinLetter = false;
        foreach (var character in token)
        {
            if (IsLatinLetter(character))
            {
                hasLatinLetter = true;
                continue;
            }

            if (!IsEnglishKeyProducingRussianLetter(character))
            {
                return script;
            }
        }

        // A standalone comma/period/quote is punctuation, not a layout word.
        // Requiring at least one Latin letter prevents ordinary punctuation
        // followed by Space from being changed into б/ю/э/ж.
        return hasLatinLetter ? TokenScript.Latin : script;
    }

    internal static bool IsEnglishKeyProducingRussianLetter(char character)
    {
        // Physical punctuation keys occupied by letters in the standard
        // Russian ЙЦУКЕН layout. Slash/backslash are deliberately absent:
        // they map to punctuation and are important URL/path protection.
        return character is '`' or '~'
            or '[' or ']'
            or ';' or ':'
            or '\'' or '"'
            or ',' or '.'
            or '<' or '>';
    }

    internal static bool HasEnglishLayoutPunctuation(string token)
        => !string.IsNullOrEmpty(token)
            && token.Any(IsEnglishKeyProducingRussianLetter);

    internal static bool IsLatinLetter(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }

    internal static bool IsCyrillicLetter(char character)
    {
        return character is >= 'А' and <= 'я' or 'Ё' or 'ё';
    }
}
