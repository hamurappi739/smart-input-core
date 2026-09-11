using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Determines whether an original token should be protected from automatic mutation.
/// Exact dictionary membership and frequency confidence are intentionally separate:
/// frequency ranks candidates for unknown originals; it must not override an exact match.
/// </summary>
internal static class TrustedWordAnalyzer
{
    internal static bool IsTrustedOriginal(string token, IAutocorrectDictionary dictionary)
    {
        return IsExactKnownOriginal(token, dictionary);
    }

    /// <summary>
    /// True when the original token is an exact known word/word-form that must not be mutated.
    /// </summary>
    internal static bool IsExactKnownOriginal(string token, IAutocorrectDictionary dictionary)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(token);
        var language = ResolveLanguage(token);
        if (language is null)
        {
            return false;
        }

        if (dictionary.IsNeverAutocorrect(normalized, language.Value))
        {
            return true;
        }

        if (language == TypingLanguage.Russian)
        {
            if (RussianYeYoEquivalence.DictionaryContainsWithYeYo(normalized, dictionary))
            {
                return true;
            }

            if (normalized.Length >= 4
                && RussianYeYoEquivalence.BloomMightContainWithYeYo(normalized)
                && !LooksLikeRepeatedCharacterTypoOfKnownWord(normalized, dictionary))
            {
                return true;
            }

            return false;
        }

        if (dictionary.Contains(normalized, language.Value))
        {
            return true;
        }

        return false;
    }

    internal static bool IsKnownWord(string token, TypingLanguage language, IAutocorrectDictionary dictionary)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(token);
        if (language == TypingLanguage.Russian)
        {
            if (RussianYeYoEquivalence.DictionaryContainsWithYeYo(normalized, dictionary))
            {
                return true;
            }

            return normalized.Length >= 4
                && RussianYeYoEquivalence.BloomMightContainWithYeYo(normalized);
        }

        return dictionary.Contains(normalized, language);
    }

    internal static bool BlocksKnownToKnownSubstitution(
        string originalToken,
        string candidateToken,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        // Only exact protected originals block substitution. Bloom false-positives
        // that look like repeated-character typos must still be correctable.
        if (!IsExactKnownOriginal(originalToken, dictionary))
        {
            return false;
        }

        // A dictionary may contain the unaccented/legacy spelling `...еш`,
        // but in ordinary modern text the terminal soft-sign completion is a
        // deterministic orthographic repair (`...еш` -> `...ешь`).
        if (RussianOrthographyHeuristics.IsLikelyTerminalSoftSignOmission(
                originalToken,
                language))
        {
            return false;
        }

        if (!IsKnownWord(candidateToken, language, dictionary))
        {
            return false;
        }

        // Never auto-rewrite between е/ё spellings of the same word.
        if (language == TypingLanguage.Russian
            && AreYeYoVariantsOnly(
                AutocorrectDictionaryNormalizer.NormalizeLookupKey(originalToken),
                AutocorrectDictionaryNormalizer.NormalizeLookupKey(candidateToken)))
        {
            return true;
        }

        return HasSingleEditOrTransposeRelationship(
            AutocorrectDictionaryNormalizer.NormalizeLookupKey(originalToken),
            AutocorrectDictionaryNormalizer.NormalizeLookupKey(candidateToken));
    }

    internal static bool ShouldBlockLayoutConversion(
        string originalToken,
        IAutocorrectDictionary dictionary)
    {
        if (string.IsNullOrWhiteSpace(originalToken))
        {
            return true;
        }

        var script = TokenScriptAnalyzer.Classify(originalToken);
        if (script == TokenScript.Latin)
        {
            var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(originalToken);

            if (LayoutServiceWordWhitelist.TryGetRussianReplacement(originalToken, out _))
            {
                return false;
            }

            if (dictionary.Contains(normalized, TypingLanguage.English))
            {
                return true;
            }

            return false;
        }

        return IsExactKnownOriginal(originalToken, dictionary);
    }

    internal static bool HasStrongRussianSpellingExplanation(
        string token,
        IAutocorrectDictionary dictionary,
        AutocorrectionResult spellingResult)
    {
        if (TokenScriptAnalyzer.Classify(token) != TokenScript.Cyrillic)
        {
            return false;
        }

        if (spellingResult.Recommendation != AutocorrectionRecommendation.Candidate
            || string.IsNullOrEmpty(spellingResult.CandidateToken))
        {
            return false;
        }

        return spellingResult.ConfidenceScore >= AutocorrectionOptions.DefaultCandidateThreshold;
    }

    internal static bool IsKnownTargetLanguageWord(
        string token,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        return IsKnownWord(token, language, dictionary);
    }

    private static bool LooksLikeRepeatedCharacterTypoOfKnownWord(
        string normalizedWord,
        IAutocorrectDictionary dictionary)
    {
        for (var index = 0; index < normalizedWord.Length - 1; index++)
        {
            if (normalizedWord[index] != normalizedWord[index + 1])
            {
                continue;
            }

            var reduced = normalizedWord.Remove(index, 1);
            if (RussianYeYoEquivalence.DictionaryContainsWithYeYo(reduced, dictionary))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreYeYoVariantsOnly(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var sawYeYoDifference = false;
        for (var index = 0; index < left.Length; index++)
        {
            var a = left[index];
            var b = right[index];
            if (a == b)
            {
                continue;
            }

            if ((a is 'е' or 'ё') && (b is 'е' or 'ё'))
            {
                sawYeYoDifference = true;
                continue;
            }

            return false;
        }

        return sawYeYoDifference;
    }

    private static TypingLanguage? ResolveLanguage(string token)
    {
        return TokenScriptAnalyzer.Classify(token) switch
        {
            TokenScript.Latin => TypingLanguage.English,
            TokenScript.Cyrillic => TypingLanguage.Russian,
            _ => null,
        };
    }

    private static bool HasSingleEditOrTransposeRelationship(string left, string right)
    {
        var lengthDelta = Math.Abs(left.Length - right.Length);
        if (lengthDelta > 1)
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return false;
        }

        if (lengthDelta == 0)
        {
            var mismatches = 0;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    mismatches++;
                }
            }

            return mismatches is 1 or 2;
        }

        var longer = left.Length > right.Length ? left : right;
        var shorter = left.Length > right.Length ? right : left;

        var edits = 0;
        var shortIndex = 0;
        for (var longIndex = 0; longIndex < longer.Length && shortIndex < shorter.Length; longIndex++)
        {
            if (longer[longIndex] == shorter[shortIndex])
            {
                shortIndex++;
                continue;
            }

            edits++;
            if (edits > 1)
            {
                return false;
            }
        }

        return true;
    }
}
