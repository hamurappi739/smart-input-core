using SmartInput.Core.Configuration;

namespace SmartInput.Core.Engines;

internal static class UnknownLatinNameLayoutEvaluator
{
    private const int MinCandidateLength = 3;
    private const int MaxCandidateLength = 20;
    private const double MinEnglishPlausibility = 0.70;
    private const double MinEnglishPlausibilityLongToken = 0.78;

    internal static bool TryScore(
        string originalToken,
        string candidateToken,
        bool isKnownRussianSourceWord,
        bool isNeverAutocorrectCandidate,
        out double score)
    {
        score = 0.0;

        if (isKnownRussianSourceWord || isNeverAutocorrectCandidate)
        {
            return false;
        }

        if (!IsEligibleSourceToken(originalToken) || !IsEligibleLatinCandidate(candidateToken))
        {
            return false;
        }

        var originalPlausibility = TokenPlausibilityAnalyzer.ScoreRussian(originalToken);
        var candidatePlausibility = TokenPlausibilityAnalyzer.ScoreEnglish(candidateToken);
        var minPlausibility = candidateToken.Length <= 4
            ? MinEnglishPlausibility
            : MinEnglishPlausibilityLongToken;

        if (candidatePlausibility < minPlausibility)
        {
            return false;
        }

        var improvement = candidatePlausibility - originalPlausibility;
        var hasFullLetterRemap = HasFullLetterRemap(originalToken, candidateToken);

        // Short tech-style names (veo, gpu) may remap fully; longer tokens need
        // a clear plausibility improvement and cannot rely on remap alone.
        if (candidateToken.Length >= 5)
        {
            if (improvement < 0.18)
            {
                return false;
            }

            if (originalPlausibility >= 0.55 && improvement < 0.25)
            {
                return false;
            }
        }
        else if (!hasFullLetterRemap && improvement < 0.12)
        {
            return false;
        }

        if (!IsNoticeablyBetterCandidate(
                originalPlausibility,
                candidatePlausibility,
                improvement,
                hasFullLetterRemap,
                candidateToken.Length))
        {
            return false;
        }

        score = BuildConservativeScore(
            originalToken.Length,
            originalPlausibility,
            candidatePlausibility,
            improvement,
            hasFullLetterRemap);

        return score >= WrongLayoutDetectionOptions.UnknownLatinNameCandidateThreshold;
    }

    private static bool IsEligibleSourceToken(string token)
    {
        if (token.Length is < MinCandidateLength or > MaxCandidateLength)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!TokenScriptAnalyzer.IsCyrillicLetter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEligibleLatinCandidate(string token)
    {
        if (token.Length is < MinCandidateLength or > MaxCandidateLength)
        {
            return false;
        }

        var hasOrdinaryVowel = false;
        var hasY = false;
        foreach (var character in token)
        {
            if (!TokenScriptAnalyzer.IsLatinLetter(character))
            {
                return false;
            }

            if (IsOrdinaryEnglishVowel(character))
            {
                hasOrdinaryVowel = true;
            }
            else if (character is 'y' or 'Y')
            {
                hasY = true;
            }
        }

        // y alone is not a sufficient English vowel for unknown-name acceptance.
        if (!hasOrdinaryVowel)
        {
            return false;
        }

        if (hasY && !hasOrdinaryVowel)
        {
            return false;
        }

        return !HasImpossibleEnglishClusters(token);
    }

    private static bool IsNoticeablyBetterCandidate(
        double originalPlausibility,
        double candidatePlausibility,
        double improvement,
        bool hasFullLetterRemap,
        int candidateLength)
    {
        if (candidateLength <= 4
            && hasFullLetterRemap
            && candidatePlausibility >= MinEnglishPlausibility)
        {
            return true;
        }

        if (candidatePlausibility >= 0.80 && improvement >= 0.12)
        {
            return true;
        }

        if (originalPlausibility < 0.50 && candidatePlausibility >= 0.78)
        {
            return true;
        }

        return improvement >= 0.22;
    }

    private static bool HasFullLetterRemap(string originalToken, string candidateToken)
    {
        if (originalToken.Length != candidateToken.Length)
        {
            return false;
        }

        for (var index = 0; index < originalToken.Length; index++)
        {
            if (originalToken[index] == candidateToken[index])
            {
                return false;
            }
        }

        return true;
    }

    private static double BuildConservativeScore(
        int originalLength,
        double originalPlausibility,
        double candidatePlausibility,
        double improvement,
        bool hasFullLetterRemap)
    {
        var score = 0.70;
        score += Math.Clamp(candidatePlausibility - 0.60, 0.0, 0.22) * 0.45;
        score += Math.Clamp(1.0 - originalPlausibility, 0.0, 0.45) * 0.18;
        score += Math.Clamp(improvement, 0.0, 0.35) * 0.22;

        if (hasFullLetterRemap && originalLength <= 4)
        {
            score += 0.04;
        }

        if (originalLength is >= 3 and <= 4)
        {
            score += 0.03;
        }

        return Math.Clamp(
            score,
            WrongLayoutDetectionOptions.UnknownLatinNameCandidateThreshold,
            WrongLayoutDetectionOptions.UnknownLatinNameMaxConfidence);
    }

    private static bool HasImpossibleEnglishClusters(string token)
    {
        var consecutiveConsonants = 0;
        foreach (var character in token)
        {
            if (IsOrdinaryEnglishVowel(character) || character is 'y' or 'Y')
            {
                consecutiveConsonants = 0;
                continue;
            }

            consecutiveConsonants++;
            // Longer unknown names must not contain long consonant runs.
            if (consecutiveConsonants >= (token.Length <= 4 ? 4 : 3))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOrdinaryEnglishVowel(char character)
    {
        return character is 'a' or 'e' or 'i' or 'o' or 'u'
            or 'A' or 'E' or 'I' or 'O' or 'U';
    }
}
