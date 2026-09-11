using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

internal static class AutocorrectionCandidateRanker
{
    internal static double ScoreCandidate(
        string originalToken,
        GeneratedAutocorrectionCandidate candidate,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        bool originalIsTrustedWord)
    {
        if (!dictionary.Contains(candidate.Word, language))
        {
            return 0.0;
        }

        if (TrustedWordAnalyzer.BlocksKnownToKnownSubstitution(
                originalToken,
                candidate.Word,
                language,
                dictionary))
        {
            return 0.0;
        }

        var originalPlausibility = ScorePlausibility(originalToken, language);
        var candidatePlausibility = ScorePlausibility(candidate.Word, language);
        var improvement = candidatePlausibility - originalPlausibility;

        if (improvement <= 0.01)
        {
            if (!originalIsTrustedWord
                && candidate.EditCost <= 1.0
                && HasSingleEditRelationship(originalToken, candidate.Word))
            {
                improvement = 0.10;
            }
            else
            {
                return 0.0;
            }
        }

        var editScore = Math.Max(0.20, 1.0 - (candidate.EditCost * 0.50));
        if (candidate.UsedAdjacentKeys)
        {
            editScore = Math.Min(1.0, editScore + 0.06);
        }

        var frequency = dictionary.GetFrequency(candidate.Word, language);
        var frequencyScore = frequency > 0
            ? Math.Min(0.22, frequency * 0.22)
            : 0.03;

        var score = 0.22 * editScore
            + 0.26 * candidatePlausibility
            + 0.26 * Math.Clamp(improvement, 0.0, 0.55)
            + frequencyScore;

        if (!originalIsTrustedWord)
        {
            score += 0.14;
        }

        if (improvement >= 0.18)
        {
            score += 0.05;
        }

        if (candidate.EditCost <= 0.76
            && originalToken.Length == candidate.Word.Length
            && HasSingleEditRelationship(originalToken, candidate.Word))
        {
            score += 0.06;
        }

        if (candidate.EditCost <= 0.76 && frequency >= 0.90)
        {
            score += 0.04;
        }

        if (candidate.EditCost <= 0.76 && IsAdjacentTransposition(originalToken, candidate.Word))
        {
            score += 0.07;
        }

        if (IsRepeatedCharacterInsertion(originalToken, candidate.Word))
        {
            score += 0.10;
        }

        if (IsRepeatedCharacterReduction(originalToken, candidate.Word))
        {
            score += 0.22;
            if (frequency >= 0.70)
            {
                score += 0.06;
            }
        }
        else if (candidate.Word.Length == originalToken.Length - 1)
        {
            // Arbitrary single-letter deletion is a major source of wrong
            // confident recoveries (абзац→бац, аборт→борт). Keep only the
            // strongest everyday short-word cleanups.
            if (frequency < 0.93 || originalToken.Length <= 4)
            {
                return 0.0;
            }

            score *= 0.78;
        }

        score += GetDictionaryTierBoost(candidate.Word, language, dictionary);

        score *= GetLengthMultiplier(originalToken.Length);

        return Math.Clamp(score, 0.0, 0.95);
    }

    internal static bool IsClearlyBetterThanValidOriginal(
        double candidateScore,
        double originalPlausibility,
        AutocorrectionOptions options)
    {
        return candidateScore >= options.CandidateThreshold
            && originalPlausibility < options.ValidWordPlausibilityThreshold
            && candidateScore - (originalPlausibility * 0.50) >= 0.16;
    }

    internal static int CompareRankedCandidates(
        RankedAutocorrectionCandidate left,
        RankedAutocorrectionCandidate right)
    {
        var scoreComparison = right.Score.CompareTo(left.Score);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        var tierComparison = right.DictionaryTier.CompareTo(left.DictionaryTier);
        if (tierComparison != 0)
        {
            return tierComparison;
        }

        var frequencyComparison = right.Frequency.CompareTo(left.Frequency);
        if (frequencyComparison != 0)
        {
            return frequencyComparison;
        }

        var editCostComparison = left.EditCost.CompareTo(right.EditCost);
        if (editCostComparison != 0)
        {
            return editCostComparison;
        }

        return string.CompareOrdinal(left.Word, right.Word);
    }

    private static double GetDictionaryTierBoost(
        string word,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        if (dictionary.IsUserDictionaryEntry(word, language))
        {
            return 0.14;
        }

        if (language == TypingLanguage.Russian && RussianWordFormBloomFilter.MightContain(word))
        {
            return 0.06;
        }

        return 0.0;
    }

    internal static int GetDictionaryTier(string word, TypingLanguage language, IAutocorrectDictionary dictionary)
    {
        if (dictionary.IsUserDictionaryEntry(word, language))
        {
            return 3;
        }

        if (dictionary.Contains(word, language))
        {
            return 2;
        }

        if (language == TypingLanguage.Russian && RussianWordFormBloomFilter.MightContain(word))
        {
            return 1;
        }

        return 0;
    }

    internal static bool IsRepeatedCharacterReduction(string originalToken, string candidateWord)
    {
        if (originalToken.Length != candidateWord.Length + 1)
        {
            return false;
        }

        for (var index = 0; index < originalToken.Length - 1; index++)
        {
            if (originalToken[index] != originalToken[index + 1])
            {
                continue;
            }

            if (string.Equals(originalToken.Remove(index, 1), candidateWord, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAdjacentTransposition(string originalToken, string candidateWord)
    {
        if (originalToken.Length != candidateWord.Length || originalToken.Length < 2)
        {
            return false;
        }

        for (var index = 0; index < originalToken.Length - 1; index++)
        {
            if (originalToken[index] == candidateWord[index])
            {
                continue;
            }

            if (originalToken[index] == candidateWord[index + 1]
                && originalToken[index + 1] == candidateWord[index]
                && string.Equals(
                    originalToken[..index] + originalToken[(index + 2)..],
                    candidateWord[..index] + candidateWord[(index + 2)..],
                    StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool HasSingleEditRelationship(string originalToken, string candidateWord)
    {
        var lengthDelta = Math.Abs(originalToken.Length - candidateWord.Length);
        if (lengthDelta > 1)
        {
            return false;
        }

        if (string.Equals(originalToken, candidateWord, StringComparison.Ordinal))
        {
            return false;
        }

        if (lengthDelta == 0)
        {
            var mismatches = 0;
            for (var index = 0; index < originalToken.Length; index++)
            {
                if (originalToken[index] != candidateWord[index])
                {
                    mismatches++;
                }
            }

            return mismatches is 1 or 2;
        }

        var longer = originalToken.Length > candidateWord.Length ? originalToken : candidateWord;
        var shorter = originalToken.Length > candidateWord.Length ? candidateWord : originalToken;

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

    private static bool IsRepeatedCharacterInsertion(string originalToken, string candidateWord)
    {
        if (candidateWord.Length != originalToken.Length + 1)
        {
            return false;
        }

        for (var index = 0; index < candidateWord.Length - 1; index++)
        {
            if (candidateWord[index] != candidateWord[index + 1])
            {
                continue;
            }

            var withoutFirst = candidateWord.Remove(index, 1);
            if (string.Equals(withoutFirst, originalToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static double ScorePlausibility(string token, TypingLanguage language)
    {
        return language switch
        {
            TypingLanguage.English => TokenPlausibilityAnalyzer.ScoreEnglish(token),
            TypingLanguage.Russian => TokenPlausibilityAnalyzer.ScoreRussian(token),
            _ => 0.0,
        };
    }

    private static double GetLengthMultiplier(int length)
    {
        return length switch
        {
            <= 1 => 0.0,
            2 => 0.60,
            3 => 0.88,
            4 => 1.0,
            _ => 1.0,
        };
    }
}

internal readonly struct RankedAutocorrectionCandidate
{
    public RankedAutocorrectionCandidate(
        string word,
        double score,
        double frequency,
        int dictionaryTier,
        double editCost)
    {
        Word = word;
        Score = score;
        Frequency = frequency;
        DictionaryTier = dictionaryTier;
        EditCost = editCost;
    }

    public string Word { get; }

    public double Score { get; }

    public double Frequency { get; }

    public int DictionaryTier { get; }

    public double EditCost { get; }
}
