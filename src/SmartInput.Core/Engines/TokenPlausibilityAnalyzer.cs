namespace SmartInput.Core.Engines;

internal static class TokenPlausibilityAnalyzer
{
    private const int MaxConsecutiveConsonants = 3;

    internal static double ScoreEnglish(string token)
    {
        if (!IsAllLatinLetters(token))
        {
            return 0.0;
        }

        return ScoreLanguageWord(token, IsEnglishVowel, IsEnglishConsonant);
    }

    internal static double ScoreRussian(string token)
    {
        if (!IsAllCyrillicLetters(token))
        {
            return 0.0;
        }

        return ScoreLanguageWord(token, IsRussianVowel, IsRussianConsonant);
    }

    private static double ScoreLanguageWord(
        string token,
        Func<char, bool> isVowel,
        Func<char, bool> isConsonant)
    {
        var length = token.Length;
        if (length == 0)
        {
            return 0.0;
        }

        if (length == 1)
        {
            return isVowel(token[0]) ? 0.45 : 0.25;
        }

        var vowelCount = 0;
        var maxConsecutiveConsonants = 0;
        var currentConsecutiveConsonants = 0;

        foreach (var character in token)
        {
            if (isVowel(character))
            {
                vowelCount++;
                currentConsecutiveConsonants = 0;
                continue;
            }

            if (!isConsonant(character))
            {
                continue;
            }

            currentConsecutiveConsonants++;
            if (currentConsecutiveConsonants > maxConsecutiveConsonants)
            {
                maxConsecutiveConsonants = currentConsecutiveConsonants;
            }
        }

        if (vowelCount == 0)
        {
            return 0.12;
        }

        var score = 0.72;

        if (length >= 3)
        {
            score += 0.08;
        }

        if (length >= 5)
        {
            score += 0.05;
        }

        var vowelRatio = (double)vowelCount / length;
        if (vowelRatio is >= 0.25 and <= 0.60)
        {
            score += 0.05;
        }
        else if (vowelRatio < 0.25)
        {
            score -= 0.16;
        }

        if (maxConsecutiveConsonants > MaxConsecutiveConsonants)
        {
            score -= 0.22;
        }
        else if (maxConsecutiveConsonants == MaxConsecutiveConsonants)
        {
            score -= 0.10;
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    private static bool IsAllLatinLetters(string token)
    {
        foreach (var character in token)
        {
            if (!TokenScriptAnalyzer.IsLatinLetter(character))
            {
                return false;
            }
        }

        return token.Length > 0;
    }

    private static bool IsAllCyrillicLetters(string token)
    {
        foreach (var character in token)
        {
            if (!TokenScriptAnalyzer.IsCyrillicLetter(character))
            {
                return false;
            }
        }

        return token.Length > 0;
    }

    private static bool IsEnglishVowel(char character)
    {
        return character is 'a' or 'e' or 'i' or 'o' or 'u' or 'y'
            or 'A' or 'E' or 'I' or 'O' or 'U' or 'Y';
    }

    private static bool IsEnglishConsonant(char character)
    {
        return TokenScriptAnalyzer.IsLatinLetter(character) && !IsEnglishVowel(character);
    }

    private static bool IsRussianVowel(char character)
    {
        return character is 'а' or 'е' or 'ё' or 'и' or 'о' or 'у' or 'ы' or 'э' or 'ю' or 'я'
            or 'А' or 'Е' or 'Ё' or 'И' or 'О' or 'У' or 'Ы' or 'Э' or 'Ю' or 'Я';
    }

    private static bool IsRussianConsonant(char character)
    {
        return TokenScriptAnalyzer.IsCyrillicLetter(character) && !IsRussianVowel(character);
    }
}
