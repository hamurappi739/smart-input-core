using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Production-only bounded candidate safety lookup. Uses signature indexes,
/// not the corpus verification oracle.
/// </summary>
internal static class ProductionCandidateSafetyClassifier
{
    internal static bool AllowsSpellingApply(
        string token,
        string replacement,
        TypingLanguage sourceLanguage,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        CandidateAmbiguityIndex ambiguityIndex)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(replacement))
        {
            return false;
        }

        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary))
        {
            return false;
        }

        var normalized = token.ToLowerInvariant();
        var target = replacement.ToLowerInvariant();
        var targetFrequency = dictionary.GetFrequency(target, sourceLanguage);
        if (!dictionary.Contains(target, sourceLanguage) || targetFrequency < 0.65)
        {
            return false;
        }

        var generated = AutocorrectionCandidateGenerator.Generate(normalized, sourceLanguage, options);
        var matched = generated.FirstOrDefault(candidate =>
            string.Equals(candidate.Word, target, StringComparison.OrdinalIgnoreCase));

        var proposedCost = string.IsNullOrEmpty(matched.Word)
            ? EstimateEditCost(normalized, target, sourceLanguage)
            : matched.EditCost;

        if (AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(normalized, target))
        {
            if (!AllowsRepeatedCharacterApply(normalized, target, sourceLanguage, dictionary))
            {
                return false;
            }
        }

        var hasDecisivePrefixRecovery =
            IsDecisivePrefixMissingCharacterRecovery(
                normalized,
                target,
                sourceLanguage,
                dictionary,
                targetFrequency);

        if (!hasDecisivePrefixRecovery
            && ambiguityIndex.HasAmbiguousCompetition(
                normalized,
                sourceLanguage,
                target,
                proposedCost,
                targetFrequency))
        {
            return false;
        }

        return true;
    }

    private static bool IsDecisivePrefixMissingCharacterRecovery(
        string token,
        string replacement,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        double replacementFrequency)
    {
        // The compact signature index intentionally errs on the side of Wait.
        // A narrowly defined prefix omission is safe to release when the target
        // is a frequent lexicon word: the candidate is structurally exact and
        // the missing character is at a single, deterministic position.
        if (language != TypingLanguage.Russian
            || token.Length < 4
            || replacement.Length != token.Length + 1
            || replacement[1..] != token
            || replacementFrequency < 0.80
            || dictionary.IsNeverAutocorrect(replacement, language))
        {
            return false;
        }

        return EditOperationClassifier.Classify(token, replacement, language)
            == EditOperationType.MissingCharacter;
    }

    private static bool AllowsRepeatedCharacterApply(
        string token,
        string replacement,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary))
        {
            return false;
        }

        if (!dictionary.Contains(replacement, language)
            || dictionary.GetFrequency(replacement, language) < 0.65)
        {
            return false;
        }

        var runTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < token.Length - 1; index++)
        {
            if (token[index] != token[index + 1])
            {
                continue;
            }

            var reduced = token.Remove(index, 1);
            if (dictionary.Contains(reduced, language)
                && dictionary.GetFrequency(reduced, language) >= 0.65)
            {
                runTargets.Add(reduced);
            }
        }

        return runTargets.Count == 1 && runTargets.Contains(replacement);
    }

    internal static bool HasCrossOperationCompetition(
        string token,
        string replacement,
        TypingLanguage sourceLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        AutocorrectionOptions options)
    {
        var normalized = token.ToLowerInvariant();
        var target = replacement.ToLowerInvariant();
        var targetFrequency = dictionary.GetFrequency(target, sourceLanguage);
        if (targetFrequency < 0.65)
        {
            return false;
        }

        var direction = sourceLanguage == TypingLanguage.Russian
            ? LayoutConversionDirection.RussianToEnglish
            : LayoutConversionDirection.EnglishToRussian;
        var layoutMapped = layoutConverter.Convert(normalized, direction);
        if (!string.Equals(layoutMapped, normalized, StringComparison.Ordinal))
        {
            var layoutLanguage = sourceLanguage == TypingLanguage.Russian
                ? TypingLanguage.English
                : TypingLanguage.Russian;
            if (dictionary.Contains(layoutMapped, layoutLanguage)
                && dictionary.GetFrequency(layoutMapped, layoutLanguage) >= 0.70
                && !string.Equals(layoutMapped, target, StringComparison.OrdinalIgnoreCase)
                && TokenScriptAnalyzer.Classify(layoutMapped) == TokenScriptAnalyzer.Classify(target))
            {
                var layoutFreq = dictionary.GetFrequency(layoutMapped, layoutLanguage);
                if (layoutFreq >= targetFrequency - 0.12)
                {
                    return true;
                }
            }

            if (!TrustedWordAnalyzer.IsExactKnownOriginal(layoutMapped, dictionary))
            {
                var combinedOracle = MutationClassificationOracle.Analyze(
                    layoutMapped,
                    sourceLanguage == TypingLanguage.Russian ? TypingLanguage.English : TypingLanguage.Russian,
                    dictionary,
                    options);
                if (combinedOracle.Class == MutationOracleClass.UniquelyRecoverable
                    && !string.IsNullOrEmpty(combinedOracle.UniqueTarget)
                    && !string.Equals(combinedOracle.UniqueTarget, target, StringComparison.OrdinalIgnoreCase)
                    && TokenScriptAnalyzer.Classify(combinedOracle.UniqueTarget)
                        == TokenScriptAnalyzer.Classify(target)
                    && dictionary.GetFrequency(combinedOracle.UniqueTarget, layoutLanguage) >= targetFrequency - 0.12)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double EstimateEditCost(string mutation, string source, TypingLanguage language)
    {
        var operation = EditOperationClassifier.Classify(mutation, source, language);
        return operation switch
        {
            EditOperationType.RepeatedAccidentalCharacter => 0.35,
            EditOperationType.AdjacentTransposition => 0.75,
            EditOperationType.MissingCharacter => 1.0,
            EditOperationType.ExtraCharacter => 0.35,
            EditOperationType.AdjacentKeySubstitution => 0.65,
            EditOperationType.VowelSubstitution => 0.80,
            EditOperationType.GeneralSubstitution => 1.0,
            _ => 1.0,
        };
    }
}
