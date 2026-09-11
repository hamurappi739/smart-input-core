using System.Collections.Frozen;
using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

/// <summary>
/// Removes exactly one accidental ASCII space immediately before safe punctuation.
/// Conservative by design: preserves URLs, paths, decimals, abbreviations and ellipsis.
/// </summary>
public sealed class PunctuationCorrectionService : IPunctuationCorrectionService
{
    private static readonly FrozenSet<char> TargetPunctuation = FrozenSet.ToFrozenSet(
        [',', '.', '!', '?', ':', ';']);

    public PunctuationCorrectionResult Evaluate(string textBeforePunctuation, char punctuationCharacter)
    {
        if (string.IsNullOrEmpty(textBeforePunctuation)
            || !TargetPunctuation.Contains(punctuationCharacter))
        {
            return PunctuationCorrectionResult.NoChange;
        }

        if (!EndsWithSingleAsciiSpace(textBeforePunctuation))
        {
            return PunctuationCorrectionResult.NoChange;
        }

        var prefix = textBeforePunctuation[..^1];
        if (prefix.Length == 0)
        {
            return PunctuationCorrectionResult.NoChange;
        }

        var charBeforeSpace = prefix[^1];

        if (ShouldPreserveSpacing(prefix, charBeforeSpace, punctuationCharacter))
        {
            return PunctuationCorrectionResult.NoChange;
        }

        return new PunctuationCorrectionResult
        {
            Recommendation = PunctuationCorrectionRecommendation.Apply,
            OriginalSegment = " ",
            ReplacementSegment = string.Empty,
            PunctuationCharacter = punctuationCharacter,
        };
    }

    private static bool EndsWithSingleAsciiSpace(string text)
    {
        if (!text.EndsWith(" ", StringComparison.Ordinal))
        {
            return false;
        }

        return text.Length == 1 || text[^2] != ' ';
    }

    private static bool ShouldPreserveSpacing(
        string prefix,
        char charBeforeSpace,
        char punctuationCharacter)
    {
        if (charBeforeSpace == punctuationCharacter)
        {
            return true;
        }

        if (punctuationCharacter == '.' && charBeforeSpace == '.')
        {
            return true;
        }

        if (punctuationCharacter == '.' && char.IsDigit(charBeforeSpace))
        {
            return LooksLikeDecimalOrVersionSuffix(prefix);
        }

        if (LooksLikeUrlOrEmailSuffix(prefix))
        {
            return true;
        }

        if (LooksLikePathSuffix(prefix))
        {
            return true;
        }

        if (LooksLikeIdentifierSuffix(prefix))
        {
            return true;
        }

        if (charBeforeSpace == '.' && LooksLikeAbbreviationEnding(prefix))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeAbbreviationEnding(string prefix)
    {
        if (prefix.Length < 4
            || prefix[^1] != '.'
            || !char.IsLetter(prefix[^2])
            || prefix[^3] != '.'
            || !char.IsLetter(prefix[^4]))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeDecimalOrVersionSuffix(string prefix)
    {
        var index = prefix.Length - 1;
        while (index >= 0 && char.IsDigit(prefix[index]))
        {
            index--;
        }

        if (index < 0 || prefix[index] != '.')
        {
            return false;
        }

        index--;
        return index >= 0 && (char.IsDigit(prefix[index]) || char.IsLetter(prefix[index]));
    }

    private static bool LooksLikeUrlOrEmailSuffix(string prefix)
    {
        var window = prefix.Length <= 120 ? prefix : prefix[^120..];

        if (window.Contains("://", StringComparison.Ordinal)
            || window.Contains('@')
            || window.Contains("www.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikePathSuffix(string prefix)
    {
        if (prefix.Contains('\\')
            || prefix.Contains("~/", StringComparison.Ordinal)
            || prefix.StartsWith("/", StringComparison.Ordinal))
        {
            return true;
        }

        if (prefix.Length >= 2
            && char.IsLetter(prefix[0])
            && prefix[1] == ':'
            && prefix.Contains('\\'))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeIdentifierSuffix(string prefix)
    {
        var end = prefix.Length - 1;
        if (end < 0)
        {
            return false;
        }

        var start = end;
        while (start >= 0 && IsIdentifierCharacter(prefix[start]))
        {
            start--;
        }

        if (start >= end)
        {
            return false;
        }

        var token = prefix[(start + 1)..];
        return token.Contains('_')
            || (ContainsLetter(token) && ContainsInternalUppercaseLetter(token));
    }

    private static bool ContainsInternalUppercaseLetter(string value)
    {
        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsUpper(value[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '-';

    private static bool ContainsLetter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }

        return false;
    }
}
