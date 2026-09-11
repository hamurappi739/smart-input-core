using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

public interface IWrongLayoutDetectionService
{
    WrongLayoutDetectionResult Evaluate(
        string token,
        ActiveLanguageSet activeLanguages,
        WrongLayoutDetectionOptions? options = null);
}

public sealed class WrongLayoutDetectionService : IWrongLayoutDetectionService
{
    private readonly ILayoutConversionService _layoutConversionService;
    private readonly IAutocorrectDictionary? _dictionary;

    public WrongLayoutDetectionService(
        ILayoutConversionService layoutConversionService,
        IAutocorrectDictionary? dictionary = null)
    {
        _layoutConversionService = layoutConversionService;
        _dictionary = dictionary;
    }

    public WrongLayoutDetectionResult Evaluate(
        string token,
        ActiveLanguageSet activeLanguages,
        WrongLayoutDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(activeLanguages);

        options ??= new WrongLayoutDetectionOptions();

        if (activeLanguages.IncludesEnglish
            && activeLanguages.IncludesRussian
            && LayoutServiceWordWhitelist.TryGetReplacement(
                token,
                out var serviceWord,
                out var serviceDirection))
        {
            return BuildResult(
                token,
                serviceWord,
                serviceDirection,
                LayoutServiceWordWhitelist.ConfidenceScore,
                LayoutDetectionRecommendation.Candidate);
        }

        if (ProtectedTokenAnalyzer.IsProtected(token))
        {
            return BuildResult(
                token,
                candidateToken: null,
                direction: null,
                confidenceScore: 0.0,
                LayoutDetectionRecommendation.NoChange);
        }

        // A single internal comma is the physical RU `б` key under the EN
        // layout. Keep regular token classification strict, but allow this
        // layout-only shape to reach the normal detector.
        var script = TokenScriptAnalyzer.ClassifyForLayout(token);
        if (!TryResolveDirection(script, activeLanguages, out var direction))
        {
            return BuildResult(
                token,
                candidateToken: null,
                direction: null,
                confidenceScore: 0.0,
                LayoutDetectionRecommendation.NoChange);
        }

        var candidateToken = direction == LayoutConversionDirection.RussianToEnglish
            && LayoutCorrectionAnchors.TryGetCanonicalEnglishReplacement(token, out var canonicalEnglish)
                ? canonicalEnglish
                : _layoutConversionService.Convert(token, direction);
        if (string.Equals(token, candidateToken, StringComparison.Ordinal))
        {
            return BuildResult(
                token,
                candidateToken,
                direction,
                confidenceScore: 0.0,
                LayoutDetectionRecommendation.NoChange);
        }

        var confidenceScore = ScoreCandidate(token, candidateToken, script, direction);
        confidenceScore = ApplyDictionaryEvidence(
            token,
            candidateToken,
            script,
            direction,
            confidenceScore);
        confidenceScore = ApplyUnknownLatinNameEvidence(
            token,
            candidateToken,
            direction,
            confidenceScore);
        var recommendation = ResolveRecommendation(token, confidenceScore, options);

        return BuildResult(token, candidateToken, direction, confidenceScore, recommendation);
    }

    private static bool TryResolveDirection(
        TokenScript script,
        ActiveLanguageSet activeLanguages,
        out LayoutConversionDirection direction)
    {
        if (!activeLanguages.IncludesEnglish || !activeLanguages.IncludesRussian)
        {
            direction = default;
            return false;
        }

        switch (script)
        {
            case TokenScript.Latin:
                direction = LayoutConversionDirection.EnglishToRussian;
                return true;
            case TokenScript.Cyrillic:
                direction = LayoutConversionDirection.RussianToEnglish;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    private static double ScoreCandidate(
        string originalToken,
        string candidateToken,
        TokenScript script,
        LayoutConversionDirection direction)
    {
        var originalPlausibility = script switch
        {
            TokenScript.Latin => TokenPlausibilityAnalyzer.ScoreEnglish(originalToken),
            TokenScript.Cyrillic => TokenPlausibilityAnalyzer.ScoreRussian(originalToken),
            _ => 0.0,
        };

        var candidatePlausibility = direction switch
        {
            LayoutConversionDirection.EnglishToRussian => TokenPlausibilityAnalyzer.ScoreRussian(candidateToken),
            LayoutConversionDirection.RussianToEnglish => TokenPlausibilityAnalyzer.ScoreEnglish(candidateToken),
            _ => 0.0,
        };

        if (originalPlausibility >= 0.70 && candidatePlausibility <= originalPlausibility)
        {
            return 0.0;
        }

        if (candidatePlausibility < 0.45)
        {
            return Math.Clamp(candidatePlausibility * 0.35, 0.0, 0.35);
        }

        var improvement = candidatePlausibility - originalPlausibility;
        if (improvement <= 0.05)
        {
            return Math.Clamp(improvement, 0.0, 0.25);
        }

        var score = 0.30;
        score += (1.0 - originalPlausibility) * 0.30;
        score += candidatePlausibility * 0.30;
        score += Math.Clamp(improvement, 0.0, 0.50) * 0.20;

        if (improvement >= 0.20)
        {
            score += 0.08;
        }

        score *= GetLengthMultiplier(originalToken.Length);
        score *= GetConversionChangeMultiplier(originalToken, candidateToken);

        return Math.Clamp(score, 0.0, 0.95);
    }

    private double ApplyDictionaryEvidence(
        string originalToken,
        string candidateToken,
        TokenScript originalScript,
        LayoutConversionDirection direction,
        double heuristicScore)
    {
        if (_dictionary is null)
        {
            return heuristicScore;
        }

        if (TrustedWordAnalyzer.ShouldBlockLayoutConversion(originalToken, _dictionary))
        {
            return 0.0;
        }

        var sourceLanguage = originalScript == TokenScript.Latin
            ? TypingLanguage.English
            : TypingLanguage.Russian;
        var targetLanguage = direction == LayoutConversionDirection.EnglishToRussian
            ? TypingLanguage.Russian
            : TypingLanguage.English;

        if (sourceLanguage == TypingLanguage.English)
        {
            if (_dictionary.Contains(originalToken, TypingLanguage.English))
            {
                return 0.0;
            }
        }
        else if (TrustedWordAnalyzer.IsExactKnownOriginal(originalToken, _dictionary))
        {
            return 0.0;
        }

        var candidateFrequency = _dictionary.GetFrequency(candidateToken, targetLanguage);
        if (_dictionary.Contains(candidateToken, targetLanguage)
            && candidateFrequency >= 0.55
            && !_dictionary.IsNeverAutocorrect(candidateToken, targetLanguage))
        {
            return Math.Max(heuristicScore, 0.86);
        }

        return heuristicScore;
    }

    private double ApplyUnknownLatinNameEvidence(
        string originalToken,
        string candidateToken,
        LayoutConversionDirection direction,
        double currentScore)
    {
        if (direction != LayoutConversionDirection.RussianToEnglish)
        {
            return currentScore;
        }

        if (_dictionary is not null && TrustedWordAnalyzer.IsExactKnownOriginal(originalToken, _dictionary))
        {
            return currentScore;
        }

        if (currentScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold)
        {
            return currentScore;
        }

        var isKnownRussianSource = _dictionary is not null
            && TrustedWordAnalyzer.IsExactKnownOriginal(originalToken, _dictionary);
        var isNeverAutocorrectCandidate = _dictionary?.IsNeverAutocorrect(candidateToken, TypingLanguage.English) == true;

        if (!UnknownLatinNameLayoutEvaluator.TryScore(
                originalToken,
                candidateToken,
                isKnownRussianSource,
                isNeverAutocorrectCandidate,
                out var unknownScore))
        {
            return currentScore;
        }

        return Math.Max(currentScore, unknownScore);
    }

    private static double GetLengthMultiplier(int length)
    {
        return length switch
        {
            <= 1 => 0.0,
            2 => 0.45,
            3 => 0.70,
            4 => 0.85,
            _ => 1.0,
        };
    }

    private static double GetConversionChangeMultiplier(string originalToken, string candidateToken)
    {
        var letterCount = 0;
        var changedLetters = 0;

        for (var index = 0; index < originalToken.Length; index++)
        {
            var originalCharacter = originalToken[index];
            if (!char.IsLetter(originalCharacter))
            {
                continue;
            }

            letterCount++;
            if (index >= candidateToken.Length || candidateToken[index] != originalCharacter)
            {
                changedLetters++;
            }
        }

        if (letterCount == 0)
        {
            return 0.5;
        }

        var changeRatio = (double)changedLetters / letterCount;
        return changeRatio switch
        {
            >= 0.80 => 1.0,
            >= 0.50 => 0.90,
            _ => 0.65,
        };
    }

    private static LayoutDetectionRecommendation ResolveRecommendation(
        string token,
        double confidenceScore,
        WrongLayoutDetectionOptions options)
    {
        if (confidenceScore >= options.CandidateThreshold)
        {
            return LayoutDetectionRecommendation.Candidate;
        }

        if (confidenceScore >= options.WaitThreshold)
        {
            return LayoutDetectionRecommendation.Wait;
        }

        return LayoutDetectionRecommendation.NoChange;
    }

    private static WrongLayoutDetectionResult BuildResult(
        string originalToken,
        string? candidateToken,
        LayoutConversionDirection? direction,
        double confidenceScore,
        LayoutDetectionRecommendation recommendation)
    {
        return new WrongLayoutDetectionResult
        {
            OriginalToken = originalToken,
            CandidateToken = candidateToken,
            ConversionDirection = direction,
            ConfidenceScore = confidenceScore,
            Recommendation = recommendation,
        };
    }
}
