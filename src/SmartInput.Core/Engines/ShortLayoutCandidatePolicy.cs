using System.Collections.Frozen;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Engines;

/// <summary>
/// Conservative exact-layout policy for complete words of one to four keys.
/// Every token in that range is evaluated, but only a known target with
/// sufficient frequency or a clear script-plausibility advantage may apply.
/// </summary>
internal static class ShortLayoutCandidatePolicy
{
    internal const int MinimumLength = 1;
    internal const int MaximumLength = 4;
    internal const double UltraFrequentTargetThreshold = 0.9992;
    internal const double MinimumUltraFrequentPlausibilityGain = 0.04;

    // A short ambiguous target may run without sentence context only when it
    // belongs to the closed-class pronoun/function-word vocabulary. Ordinary
    // short verbs (for example, "get") still require context so a spelling
    // mutation cannot be mistaken for a layout switch.
    private static readonly FrozenSet<string> AutonomousEnglishShortTargets =
        new[]
        {
            "he", "her", "hers", "him", "his", "i", "it", "its", "me", "my",
            "our", "ours", "she", "them", "they", "this", "that", "the", "us",
            "we", "who", "whom", "you", "your",
        }.ToFrozenSet(StringComparer.Ordinal);

    internal static bool IsInScope(string token)
        => token.Length is >= MinimumLength and <= MaximumLength;

    internal static bool IsUltraFrequentTarget(
        string replacement,
        TypingLanguage targetLanguage,
        IAutocorrectDictionary dictionary)
        => IsInScope(replacement)
            && dictionary.Contains(replacement, targetLanguage)
            && dictionary.GetFrequency(replacement, targetLanguage)
                >= UltraFrequentTargetThreshold;

    internal static bool IsUltraFrequentPlausibleConversion(
        string token,
        string replacement,
        TypingLanguage sourceLanguage,
        TypingLanguage targetLanguage,
        IAutocorrectDictionary dictionary)
    {
        if (!IsUltraFrequentTarget(replacement, targetLanguage, dictionary))
        {
            return false;
        }

        var sourcePlausibility = sourceLanguage == TypingLanguage.English
            ? TokenPlausibilityAnalyzer.ScoreEnglish(token)
            : TokenPlausibilityAnalyzer.ScoreRussian(token);
        var targetPlausibility = targetLanguage == TypingLanguage.English
            ? TokenPlausibilityAnalyzer.ScoreEnglish(replacement)
            : TokenPlausibilityAnalyzer.ScoreRussian(replacement);

        return targetPlausibility - sourcePlausibility
            >= MinimumUltraFrequentPlausibilityGain;
    }

    internal static bool AllowsAutonomousUltraFrequentTarget(
        string token,
        string replacement,
        TypingLanguage sourceLanguage,
        TypingLanguage targetLanguage,
        IAutocorrectDictionary dictionary)
        => targetLanguage == TypingLanguage.English
            && AutonomousEnglishShortTargets.Contains(replacement)
            && IsUltraFrequentPlausibleConversion(
                token,
                replacement,
                sourceLanguage,
                targetLanguage,
                dictionary);

    internal static bool HasTargetLanguageContext(
        TypingLanguage sourceLanguage,
        SentenceLanguageHint? languageHint)
    {
        if (languageHint is null)
        {
            return false;
        }

        if (sourceLanguage == TypingLanguage.English)
        {
            return languageHint.Value.HasStrongRussian;
        }

        return languageHint.Value.HasStrongEnglish
            || (languageHint.Value.EnglishTokenCount >= 1
                && languageHint.Value.RussianTokenCount == 0);
    }

    internal static bool AllowsKnownSourceInContext(
        string token,
        string replacement,
        TypingLanguage sourceLanguage,
        IAutocorrectDictionary dictionary,
        SentenceLanguageHint? languageHint)
    {
        var targetLanguage = sourceLanguage == TypingLanguage.English
            ? TypingLanguage.Russian
            : TypingLanguage.English;

        return IsInScope(token)
            && dictionary.Contains(token, sourceLanguage)
            && !dictionary.IsNeverAutocorrect(token, sourceLanguage)
            && !dictionary.IsUserDictionaryEntry(token, sourceLanguage)
            && IsUltraFrequentTarget(replacement, targetLanguage, dictionary)
            && HasTargetLanguageContext(sourceLanguage, languageHint);
    }

    internal static bool AllowsExactCandidate(
        string token,
        string replacement,
        TypingLanguage sourceLanguage,
        IAutocorrectDictionary dictionary,
        SentenceLanguageHint? languageHint = null)
    {
        if (!IsInScope(token) || replacement.Length != token.Length)
        {
            return false;
        }

        var targetLanguage = sourceLanguage == TypingLanguage.English
            ? TypingLanguage.Russian
            : TypingLanguage.English;

        if (dictionary.IsNeverAutocorrect(token, sourceLanguage)
            || dictionary.IsUserDictionaryEntry(token, sourceLanguage)
            || !dictionary.Contains(replacement, targetLanguage)
            || dictionary.IsNeverAutocorrect(replacement, targetLanguage))
        {
            return false;
        }

        // Direct corpus words in the source language are real words, not
        // wrong-layout noise. For dictionaries without the optional direct-
        // frequency capability, ordinary membership remains fail-closed.
        if (dictionary is IDirectFrequencyAutocorrectDictionary sourceDirect)
        {
            if (sourceDirect.HasDirectFrequency(token, sourceLanguage))
            {
                return false;
            }
        }
        else if (dictionary.Contains(token, sourceLanguage))
        {
            return false;
        }

        // Morphology-only membership is useful for preserving forms, but it
        // is too broad to authorize a short automatic layout replacement.
        if (dictionary is IDirectFrequencyAutocorrectDictionary targetDirect
            && !targetDirect.HasDirectFrequency(replacement, targetLanguage))
        {
            return false;
        }

        var targetFrequency = dictionary.GetFrequency(replacement, targetLanguage);
        var sourcePlausibility = sourceLanguage == TypingLanguage.English
            ? TokenPlausibilityAnalyzer.ScoreEnglish(token)
            : TokenPlausibilityAnalyzer.ScoreRussian(token);
        var targetPlausibility = targetLanguage == TypingLanguage.English
            ? TokenPlausibilityAnalyzer.ScoreEnglish(replacement)
            : TokenPlausibilityAnalyzer.ScoreRussian(replacement);
        var plausibilityGain = targetPlausibility - sourcePlausibility;
        var hasTargetLanguageContext = sourceLanguage == TypingLanguage.English
            ? languageHint?.HasStrongRussian == true
            : languageHint?.HasStrongEnglish == true;

        return token.Length switch
        {
            1 => targetFrequency >= 0.85 && plausibilityGain >= 0.15,
            2 => targetFrequency >= 0.90
                || (targetFrequency >= 0.75 && plausibilityGain >= 0.12),
            3 or 4 => targetFrequency >= 0.70
                || (targetFrequency >= 0.55 && hasTargetLanguageContext),
            _ => false,
        };
    }
}
