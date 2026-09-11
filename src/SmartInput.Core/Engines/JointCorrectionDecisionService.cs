using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Engines;

public interface IJointCorrectionDecisionService
{
    JointCorrectionDecisionResult Evaluate(
        string token,
        IAutocorrectDictionary dictionary,
        bool layoutEnabled,
        bool autocorrectEnabled,
        AutocorrectionOptions? autocorrectionOptions = null,
        WrongLayoutDetectionOptions? layoutOptions = null,
        SentenceLanguageHint? languageHint = null);
}

public sealed class JointCorrectionDecisionService : IJointCorrectionDecisionService
{
    private const double AmbiguousScoreGap = 0.05;
    private const double CombinedSpellingBoost = 0.04;
    private const double ContextLayoutBoost = 0.12;

    private readonly IWrongLayoutDetectionService _wrongLayoutDetectionService;
    private readonly IAutocorrectionService _autocorrectionService;
    private readonly ILayoutConversionService _layoutConversionService;

    public JointCorrectionDecisionService(
        IWrongLayoutDetectionService wrongLayoutDetectionService,
        IAutocorrectionService autocorrectionService,
        ILayoutConversionService layoutConversionService)
    {
        _wrongLayoutDetectionService = wrongLayoutDetectionService;
        _autocorrectionService = autocorrectionService;
        _layoutConversionService = layoutConversionService;
    }

    public JointCorrectionDecisionResult Evaluate(
        string token,
        IAutocorrectDictionary dictionary,
        bool layoutEnabled,
        bool autocorrectEnabled,
        AutocorrectionOptions? autocorrectionOptions = null,
        WrongLayoutDetectionOptions? layoutOptions = null,
        SentenceLanguageHint? languageHint = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(dictionary);

        autocorrectionOptions ??= new AutocorrectionOptions();
        layoutOptions ??= new WrongLayoutDetectionOptions();
        var hint = languageHint ?? SentenceLanguageHint.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return JointCorrectionDecisionResult.NoChange(token);
        }

        if (ProtectedTokenAnalyzer.IsProtected(token))
        {
            return JointCorrectionDecisionResult.NoChange(token);
        }

        // English-context protected / never-autocorrect Latin names stay unchanged.
        if (hint.HasStrongEnglish
            && TokenScriptAnalyzer.ClassifyForLayout(token) == TokenScript.Latin
            && dictionary.IsNeverAutocorrect(token.ToLowerInvariant(), TypingLanguage.English))
        {
            return JointCorrectionDecisionResult.NoChange(token);
        }

        var script = TokenScriptAnalyzer.ClassifyForLayout(token);
        var sourceLanguage = script switch
        {
            TokenScript.Latin => TypingLanguage.English,
            TokenScript.Cyrillic => TypingLanguage.Russian,
            _ => (TypingLanguage?)null,
        };

        var options = new List<ScoredCorrectionOption>();

        AutocorrectionResult? sameLanguageSpelling = null;
        if (autocorrectEnabled && sourceLanguage is not null)
        {
            sameLanguageSpelling = _autocorrectionService.Evaluate(
                token,
                sourceLanguage.Value,
                dictionary,
                autocorrectionOptions);

            var suppressEnglishSpellingForRussianLayoutImage =
                sourceLanguage == TypingLanguage.English
                && sameLanguageSpelling.Recommendation == AutocorrectionRecommendation.Candidate
                && !string.IsNullOrEmpty(sameLanguageSpelling.CandidateToken)
                && ShouldSuppressEnglishSpellingAsRussianLayoutImage(
                    token,
                    sameLanguageSpelling.CandidateToken,
                    dictionary);

            // R2: weak Cyrillic + strong EN physical image → never offer RU Autocorrect.
            var suppressRussianSpellingForStrongEnglishLayout =
                sourceLanguage == TypingLanguage.Russian
                && sameLanguageSpelling.Recommendation == AutocorrectionRecommendation.Candidate
                && !string.IsNullOrEmpty(sameLanguageSpelling.CandidateToken)
                && IsWeakCyrillicWithStrongEnglishPhysicalImage(token, dictionary, out _);

            // Context+anchor layout must not lose to same-script English spelling (e.g. nen→тут).
            var suppressSpellingForContextLayoutAnchor =
                hint.HasStrongRussian
                && LayoutCorrectionAnchors.IsPositiveAnchor(token)
                && sourceLanguage == TypingLanguage.English;

            if (sameLanguageSpelling.Recommendation == AutocorrectionRecommendation.Candidate
                && !string.IsNullOrEmpty(sameLanguageSpelling.CandidateToken)
                && !suppressEnglishSpellingForRussianLayoutImage
                && !suppressRussianSpellingForStrongEnglishLayout
                && !suppressSpellingForContextLayoutAnchor
                && !LayoutCorrectionAnchors.IsCanonicalAlias(token)
                && !TrustedWordAnalyzer.BlocksKnownToKnownSubstitution(
                    token,
                    sameLanguageSpelling.CandidateToken,
                    sourceLanguage.Value,
                    dictionary))
            {
                options.Add(new ScoredCorrectionOption(
                    sameLanguageSpelling.CandidateToken,
                    sameLanguageSpelling.ConfidenceScore + 0.06,
                    CorrectionKind.Autocorrect,
                    null,
                    null));
            }
        }

        WrongLayoutDetectionResult? layoutDetection = null;
        if (layoutEnabled)
        {
            layoutDetection = _wrongLayoutDetectionService.Evaluate(
                token,
                ActiveLanguageSet.EnglishAndRussian,
                layoutOptions);

            layoutDetection = ApplySentenceLanguageContext(
                token,
                dictionary,
                hint,
                layoutDetection);

            var contextOverridesLayoutBlock = layoutDetection.Recommendation
                    == LayoutDetectionRecommendation.Candidate
                && !string.IsNullOrEmpty(layoutDetection.CandidateToken)
                && ((hint.HasStrongRussian
                        && layoutDetection.ConversionDirection
                            == LayoutConversionDirection.EnglishToRussian
                        && !dictionary.IsNeverAutocorrect(
                            token.ToLowerInvariant(),
                            TypingLanguage.English)
                        && !dictionary.IsUserDictionaryEntry(
                            token.ToLowerInvariant(),
                            TypingLanguage.English))
                    || (layoutDetection.ConversionDirection
                            == LayoutConversionDirection.RussianToEnglish
                        && ShortLayoutCandidatePolicy.AllowsKnownSourceInContext(
                            token,
                            layoutDetection.CandidateToken,
                            TypingLanguage.Russian,
                            dictionary,
                            hint)));

            if (layoutDetection.Recommendation == LayoutDetectionRecommendation.Candidate
                && !string.IsNullOrEmpty(layoutDetection.CandidateToken)
                && layoutDetection.ConfidenceScore >= layoutOptions.CandidateThreshold
                && (contextOverridesLayoutBlock
                    || !TrustedWordAnalyzer.ShouldBlockLayoutConversion(token, dictionary))
                && !ShouldSuppressUnknownLayoutForStrongSpelling(
                    dictionary,
                    layoutDetection,
                    sameLanguageSpelling))
            {
                var targetLanguage = layoutDetection.ConversionDirection == LayoutConversionDirection.EnglishToRussian
                    ? KeyboardInputLanguage.Russian
                    : KeyboardInputLanguage.English;

                var layoutScore = layoutDetection.ConfidenceScore;
                if (IsShortTechLatinToken(layoutDetection.CandidateToken)
                    && layoutDetection.ConversionDirection == LayoutConversionDirection.RussianToEnglish)
                {
                    layoutScore = Math.Max(layoutScore, 0.93);
                }

                if (hint.HasStrongRussian
                    && layoutDetection.ConversionDirection == LayoutConversionDirection.EnglishToRussian)
                {
                    layoutScore = Math.Min(0.97, layoutScore + ContextLayoutBoost);
                }

                if (hint.HasStrongEnglish
                    && layoutDetection.ConversionDirection == LayoutConversionDirection.RussianToEnglish)
                {
                    layoutScore = Math.Min(0.97, layoutScore + ContextLayoutBoost);
                }

                options.Add(new ScoredCorrectionOption(
                    layoutDetection.CandidateToken,
                    layoutScore,
                    CorrectionKind.Layout,
                    layoutDetection.ConversionDirection,
                    targetLanguage));
            }
        }

        if (layoutEnabled
            && autocorrectEnabled
            && layoutDetection?.ConversionDirection is LayoutConversionDirection direction)
        {
            AddCombinedOptions(
                token,
                dictionary,
                direction,
                sameLanguageSpelling,
                autocorrectionOptions,
                options);
        }

        if (options.Count >= 2
            && sameLanguageSpelling is not null
            && TrustedWordAnalyzer.HasStrongRussianSpellingExplanation(token, dictionary, sameLanguageSpelling)
            && !IsWeakCyrillicWithStrongEnglishPhysicalImage(token, dictionary, out _))
        {
            options.RemoveAll(option =>
                option.Kind is CorrectionKind.Layout or CorrectionKind.Combined
                && !IsValidatedUnknownNameLayoutOption(option, token, dictionary));
        }

        if (script == TokenScript.Cyrillic
            && autocorrectEnabled
            && sourceLanguage == TypingLanguage.Russian)
        {
            var spellingOracle = MutationClassificationOracle.Analyze(
                token,
                TypingLanguage.Russian,
                dictionary,
                autocorrectionOptions);
            if (spellingOracle.Class == MutationOracleClass.UniquelyRecoverable
                && !string.IsNullOrEmpty(spellingOracle.UniqueTarget))
            {
                var hasValidatedUnknownName = options.Any(option =>
                    IsValidatedUnknownNameLayoutOption(option, token, dictionary));
                var weakCyrillicStrongEnLayout = IsWeakCyrillicWithStrongEnglishPhysicalImage(
                    token,
                    dictionary,
                    out _);
                var hasSafeExternalSpelling = sameLanguageSpelling is not null
                    && sourceLanguage is not null
                    && !string.IsNullOrEmpty(sameLanguageSpelling.CandidateToken)
                    && AllowsSpellingCandidate(
                        token,
                        sameLanguageSpelling,
                        sourceLanguage.Value,
                        dictionary,
                        autocorrectionOptions,
                        hint);

                // Keep cross-operation competitors visible when Unique was only achieved
                // by discarding another credible parent — final gate must Wait, unless the
                // validated unknown-name layout path is the intended outcome (мущ→veo).
                if (OperationPrecisionGate.HasDiscardedCredibleCompetitor(spellingOracle)
                    && !hasValidatedUnknownName
                    && !weakCyrillicStrongEnLayout
                    && !hasSafeExternalSpelling
                    && !LayoutCorrectionAnchors.IsCanonicalAlias(token))
                {
                    return JointCorrectionDecisionResult.Wait(token);
                }

                if (weakCyrillicStrongEnLayout)
                {
                    // R2: keep Layout/Combined; drop competing Russian Unique Autocorrect.
                    options.RemoveAll(option => option.Kind == CorrectionKind.Autocorrect);
                }
                else
                {
                    options.RemoveAll(option =>
                        option.Kind is CorrectionKind.Layout or CorrectionKind.Combined
                        && !string.Equals(
                            option.Replacement,
                            spellingOracle.UniqueTarget,
                            StringComparison.OrdinalIgnoreCase)
                        && !IsValidatedUnknownNameLayoutOption(option, token, dictionary));
                }
            }
        }

        if (script == TokenScript.Latin
            && autocorrectEnabled
            && sourceLanguage == TypingLanguage.English)
        {
            var spellingOracle = MutationClassificationOracle.Analyze(
                token,
                TypingLanguage.English,
                dictionary,
                autocorrectionOptions);
            if (spellingOracle.Class == MutationOracleClass.UniquelyRecoverable
                && !string.IsNullOrEmpty(spellingOracle.UniqueTarget))
            {
                var convertedRussian = _layoutConversionService.Convert(
                    token,
                    LayoutConversionDirection.EnglishToRussian);
                var combinedSpellingOracle = string.Equals(convertedRussian, token, StringComparison.Ordinal)
                    ? null
                    : MutationClassificationOracle.Analyze(
                        convertedRussian,
                        TypingLanguage.Russian,
                        dictionary,
                        autocorrectionOptions);

                var keepCombined = combinedSpellingOracle?.Class == MutationOracleClass.UniquelyRecoverable
                    && !string.IsNullOrEmpty(combinedSpellingOracle.UniqueTarget)
                    && !string.Equals(
                        combinedSpellingOracle.UniqueTarget,
                        spellingOracle.UniqueTarget,
                        StringComparison.OrdinalIgnoreCase)
                    && dictionary.GetFrequency(spellingOracle.UniqueTarget, TypingLanguage.English) < 0.995;

                options.RemoveAll(option =>
                {
                    if (LayoutCorrectionAnchors.IsPositiveAnchor(token))
                    {
                        return false;
                    }

                    if (IsValidatedUnknownNameLayoutOption(option, token, dictionary))
                    {
                        return false;
                    }

                    return option.Kind switch
                    {
                        // An unknown, low-plausibility Latin token whose
                        // physical image is an exact Russian dictionary word
                        // is a genuine reverse-layout error (for example
                        // `lfq` -> `дай`). Do not let a weak English spelling
                        // candidate such as `lf` hide that layout option.
                        CorrectionKind.Layout => !IsValidatedEnglishToRussianLayout(
                            token,
                            option,
                            dictionary),
                        CorrectionKind.Combined => !keepCombined,
                        _ => false,
                    };
                });
            }
        }

        // Before operation-specific filtering settles on a winner: cross-operation
        // competitors with different targets force Wait when Unique spelling is weak.
        if (options.Count >= 2)
        {
            var spellingOptions = options.Where(option => option.Kind == CorrectionKind.Autocorrect).ToList();
            var competingLayoutOptions = options
                .Where(option => option.Kind is CorrectionKind.Layout or CorrectionKind.Combined)
                .ToList();
            if (spellingOptions.Count >= 1
                && competingLayoutOptions.Count >= 1
                && !spellingOptions.Any(spelling =>
                    competingLayoutOptions.Any(layout =>
                        string.Equals(
                            spelling.Replacement,
                            layout.Replacement,
                            StringComparison.OrdinalIgnoreCase))))
            {
                if (sourceLanguage is not null)
                {
                    var crossOpOracle = MutationClassificationOracle.Analyze(
                        token,
                        sourceLanguage.Value,
                        dictionary,
                        autocorrectionOptions);
                    if (crossOpOracle.Class != MutationOracleClass.UniquelyRecoverable
                        || OperationPrecisionGate.HasDiscardedCredibleCompetitor(crossOpOracle)
                        || competingLayoutOptions.Any(layout =>
                            !string.Equals(
                                layout.Replacement,
                                crossOpOracle.UniqueTarget,
                                StringComparison.OrdinalIgnoreCase)
                            && !IsValidatedUnknownNameLayoutOption(layout, token, dictionary)))
                    {
                        // Prefer Wait over applying the wrong layout when spelling Unique differs.
                        if (crossOpOracle.Class == MutationOracleClass.UniquelyRecoverable
                            && !string.IsNullOrEmpty(crossOpOracle.UniqueTarget)
                            && competingLayoutOptions.Any(layout =>
                                !string.Equals(
                                    layout.Replacement,
                                    crossOpOracle.UniqueTarget,
                                    StringComparison.OrdinalIgnoreCase)))
                        {
                            options.RemoveAll(option =>
                                option.Kind is CorrectionKind.Layout or CorrectionKind.Combined
                                && !string.Equals(
                                    option.Replacement,
                                    crossOpOracle.UniqueTarget,
                                    StringComparison.OrdinalIgnoreCase)
                                && !IsValidatedUnknownNameLayoutOption(option, token, dictionary));
                        }
                    }
                }
            }
        }

        if (options.Count == 0)
        {
            if (sameLanguageSpelling?.Recommendation == AutocorrectionRecommendation.Wait
                || layoutDetection?.Recommendation == LayoutDetectionRecommendation.Wait)
            {
                return JointCorrectionDecisionResult.Wait(token);
            }

            return JointCorrectionDecisionResult.NoChange(token);
        }

        options.Sort(CompareOptions);

        if (options.Count >= 2)
        {
            var best = options[0];
            var second = options[1];
            if (best.Score - second.Score <= AmbiguousScoreGap
                && second.Score >= autocorrectionOptions.WaitThreshold)
            {
                return JointCorrectionDecisionResult.Wait(token);
            }
        }

        var winner = options[0];

        if (script == TokenScript.Cyrillic
            && winner.Kind == CorrectionKind.Layout
            && sameLanguageSpelling is not null
            && TrustedWordAnalyzer.HasStrongRussianSpellingExplanation(token, dictionary, sameLanguageSpelling)
            && !IsWeakCyrillicWithStrongEnglishPhysicalImage(token, dictionary, out _)
            && sameLanguageSpelling.ConfidenceScore + 0.04 >= winner.Score
            && !string.IsNullOrEmpty(sameLanguageSpelling.CandidateToken)
            && AllowsSpellingCandidate(
                token,
                sameLanguageSpelling,
                sourceLanguage!.Value,
                dictionary,
                autocorrectionOptions,
                hint))
        {
            return new JointCorrectionDecisionResult
            {
                OriginalToken = token,
                ReplacementToken = sameLanguageSpelling.CandidateToken,
                Recommendation = JointCorrectionRecommendation.Apply,
                Kind = CorrectionKind.Autocorrect,
                ConfidenceScore = sameLanguageSpelling.ConfidenceScore,
            };
        }

        if (!AllowsWinner(
                token,
                winner,
                sourceLanguage,
                sameLanguageSpelling,
                dictionary,
                autocorrectionOptions,
                hint))
        {
            return JointCorrectionDecisionResult.Wait(token);
        }

        return new JointCorrectionDecisionResult
        {
            OriginalToken = token,
            ReplacementToken = winner.Replacement,
            Recommendation = JointCorrectionRecommendation.Apply,
            Kind = winner.Kind,
            ConfidenceScore = winner.Score,
            LayoutDirection = winner.LayoutDirection,
            TargetInputLanguage = winner.TargetInputLanguage,
        };
    }

    private bool AllowsWinner(
        string token,
        ScoredCorrectionOption winner,
        TypingLanguage? sourceLanguage,
        AutocorrectionResult? sameLanguageSpelling,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        SentenceLanguageHint hint)
    {
        if (winner.Kind == CorrectionKind.Autocorrect
            && sameLanguageSpelling is not null
            && string.Equals(
                winner.Replacement,
                sameLanguageSpelling.CandidateToken,
                StringComparison.OrdinalIgnoreCase)
            && sourceLanguage is not null
            && AllowsSpellingCandidate(
                token,
                sameLanguageSpelling,
                sourceLanguage.Value,
                dictionary,
                options,
                hint))
        {
            return true;
        }

        return ConfidentCorrectionApplyGate.AllowsApply(
            token,
            winner.Replacement,
            winner.Kind,
            sourceLanguage,
            dictionary,
            _layoutConversionService,
            options,
            hint);
    }

    private bool AllowsSpellingCandidate(
        string token,
        AutocorrectionResult candidate,
        TypingLanguage sourceLanguage,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        SentenceLanguageHint hint)
    {
        if (!string.IsNullOrEmpty(candidate.CandidateToken)
            && ConfidentCorrectionApplyGate.AllowsApply(
                token,
                candidate.CandidateToken,
                CorrectionKind.Autocorrect,
                sourceLanguage,
                dictionary,
                _layoutConversionService,
                options,
                hint))
        {
            return true;
        }

        // External morphology/spelling is opt-in and is not used by the
        // synthetic corpus audit. Permit only a high-confidence short
        // Russian correction to a well-known local word here; the rest still
        // goes through the strict bounded apply gate. This is what lets the
        // resident provider repair real omissions/substitutions such as a
        // missing vowel or a doubled consonant without weakening the normal
        // production oracle.
        return candidate.IsExternalProviderCandidate
            && candidate.Recommendation == AutocorrectionRecommendation.Candidate
            && candidate.ConfidenceScore >= Math.Max(0.76, options.CandidateThreshold)
            && sourceLanguage == TypingLanguage.Russian
            && !string.IsNullOrEmpty(candidate.CandidateToken)
            && token.Length >= 6
            && dictionary.GetFrequency(candidate.CandidateToken, sourceLanguage) >= 0.55
            && EditDistance(
                AutocorrectDictionaryNormalizer.NormalizeLookupKey(token),
                AutocorrectDictionaryNormalizer.NormalizeLookupKey(candidate.CandidateToken)) is >= 1 and <= 2;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private WrongLayoutDetectionResult ApplySentenceLanguageContext(
        string token,
        IAutocorrectDictionary dictionary,
        SentenceLanguageHint hint,
        WrongLayoutDetectionResult? existing)
    {
        var script = TokenScriptAnalyzer.ClassifyForLayout(token);
        TypingLanguage sourceLanguage;
        TypingLanguage targetLanguage;
        LayoutConversionDirection direction;

        if (script == TokenScript.Latin
            && ShortLayoutCandidatePolicy.HasTargetLanguageContext(
                TypingLanguage.English,
                hint))
        {
            sourceLanguage = TypingLanguage.English;
            targetLanguage = TypingLanguage.Russian;
            direction = LayoutConversionDirection.EnglishToRussian;
        }
        else if (script == TokenScript.Cyrillic
                 && ShortLayoutCandidatePolicy.HasTargetLanguageContext(
                     TypingLanguage.Russian,
                     hint))
        {
            sourceLanguage = TypingLanguage.Russian;
            targetLanguage = TypingLanguage.English;
            direction = LayoutConversionDirection.RussianToEnglish;
        }
        else
        {
            return existing ?? BuildEmptyLayout(token);
        }

        if (dictionary.IsNeverAutocorrect(token.ToLowerInvariant(), sourceLanguage)
            || dictionary.IsUserDictionaryEntry(token.ToLowerInvariant(), sourceLanguage))
        {
            return BuildEmptyLayout(token);
        }

        var converted = _layoutConversionService.Convert(token, direction);
        if (string.Equals(token, converted, StringComparison.Ordinal)
            || !dictionary.Contains(converted, targetLanguage))
        {
            return existing ?? BuildEmptyLayout(token);
        }

        var sourceIsStrongEnglish = sourceLanguage == TypingLanguage.English
            && dictionary.Contains(token, TypingLanguage.English)
            && dictionary.GetFrequency(token, TypingLanguage.English) >= 0.85
            && token.Length >= 4;

        if (sourceIsStrongEnglish)
        {
            return existing ?? BuildEmptyLayout(token);
        }

        var sourceIsKnownRussian = sourceLanguage == TypingLanguage.Russian
            && dictionary.Contains(token, TypingLanguage.Russian);
        if (sourceIsKnownRussian
            && !ShortLayoutCandidatePolicy.AllowsKnownSourceInContext(
                token,
                converted,
                sourceLanguage,
                dictionary,
                hint))
        {
            return existing ?? BuildEmptyLayout(token);
        }

        var frequency = dictionary.GetFrequency(converted, targetLanguage);
        var confidence = Math.Clamp(0.78 + (frequency * 0.18) + ContextLayoutBoost, 0.0, 0.97);

        if (existing is not null
            && existing.Recommendation == LayoutDetectionRecommendation.Candidate
            && existing.ConfidenceScore >= confidence)
        {
            return existing;
        }

        return new WrongLayoutDetectionResult
        {
            OriginalToken = token,
            CandidateToken = converted,
            ConversionDirection = direction,
            ConfidenceScore = confidence,
            Recommendation = LayoutDetectionRecommendation.Candidate,
        };
    }

    private static WrongLayoutDetectionResult BuildEmptyLayout(string token)
    {
        return new WrongLayoutDetectionResult
        {
            OriginalToken = token,
            CandidateToken = null,
            ConversionDirection = null,
            ConfidenceScore = 0.0,
            Recommendation = LayoutDetectionRecommendation.NoChange,
        };
    }

    private void AddCombinedOptions(
        string token,
        IAutocorrectDictionary dictionary,
        LayoutConversionDirection direction,
        AutocorrectionResult? sameLanguageSpelling,
        AutocorrectionOptions autocorrectionOptions,
        List<ScoredCorrectionOption> options)
    {
        if (TrustedWordAnalyzer.ShouldBlockLayoutConversion(token, dictionary))
        {
            return;
        }

        if (sameLanguageSpelling is not null
            && TrustedWordAnalyzer.HasStrongRussianSpellingExplanation(token, dictionary, sameLanguageSpelling)
            && !IsWeakCyrillicWithStrongEnglishPhysicalImage(token, dictionary, out _))
        {
            return;
        }

        var converted = _layoutConversionService.Convert(token, direction);
        if (string.Equals(token, converted, StringComparison.Ordinal))
        {
            return;
        }

        var targetLanguage = direction == LayoutConversionDirection.EnglishToRussian
            ? TypingLanguage.Russian
            : TypingLanguage.English;

        if (TrustedWordAnalyzer.IsExactKnownOriginal(converted, dictionary))
        {
            if (LayoutCorrectionAnchors.IsPositiveAnchor(token))
            {
                TryAddExactLayoutConversionOption(
                    token,
                    converted,
                    direction,
                    options);
            }

            return;
        }

        var spellingAfterLayout = _autocorrectionService.Evaluate(
            converted,
            targetLanguage,
            dictionary,
            autocorrectionOptions);

        if (spellingAfterLayout.Recommendation == AutocorrectionRecommendation.Candidate
            && !string.IsNullOrEmpty(spellingAfterLayout.CandidateToken)
            && !string.Equals(converted, spellingAfterLayout.CandidateToken, StringComparison.Ordinal))
        {
            if (!LayoutCorrectionAnchors.IsPositiveAnchor(token)
                && TrustedWordAnalyzer.IsExactKnownOriginal(spellingAfterLayout.CandidateToken, dictionary))
            {
                return;
            }

            var targetInputLanguage = direction == LayoutConversionDirection.EnglishToRussian
                ? KeyboardInputLanguage.Russian
                : KeyboardInputLanguage.English;

            options.Add(new ScoredCorrectionOption(
                spellingAfterLayout.CandidateToken,
                spellingAfterLayout.ConfidenceScore + CombinedSpellingBoost,
                CorrectionKind.Combined,
                direction,
                targetInputLanguage));
            return;
        }

        TryAddExactLayoutConversionOption(token, converted, direction, options);
    }

    private void TryAddExactLayoutConversionOption(
        string token,
        string converted,
        LayoutConversionDirection direction,
        List<ScoredCorrectionOption> options)
    {
        var layoutOnly = _wrongLayoutDetectionService.Evaluate(
            token,
            ActiveLanguageSet.EnglishAndRussian);

        TryAddExactLayoutConversionOption(token, converted, direction, layoutOnly, options);
    }

    private static void TryAddExactLayoutConversionOption(
        string token,
        string converted,
        LayoutConversionDirection direction,
        WrongLayoutDetectionResult layoutOnly,
        List<ScoredCorrectionOption> options)
    {
        if (layoutOnly.Recommendation != LayoutDetectionRecommendation.Candidate
            || string.IsNullOrEmpty(layoutOnly.CandidateToken)
            || !string.Equals(layoutOnly.CandidateToken, converted, StringComparison.Ordinal)
            || layoutOnly.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold)
        {
            return;
        }

        var targetInputLanguage = direction == LayoutConversionDirection.EnglishToRussian
            ? KeyboardInputLanguage.Russian
            : KeyboardInputLanguage.English;

        if (options.Any(option =>
                option.Kind == CorrectionKind.Layout
                && string.Equals(option.Replacement, converted, StringComparison.Ordinal)))
        {
            return;
        }

        options.Add(new ScoredCorrectionOption(
            converted,
            layoutOnly.ConfidenceScore,
            CorrectionKind.Layout,
            direction,
            targetInputLanguage));
    }

    private static bool IsValidatedUnknownNameLayoutOption(
        ScoredCorrectionOption option,
        string token,
        IAutocorrectDictionary dictionary)
    {
        if (option.Kind != CorrectionKind.Layout
            || option.LayoutDirection != LayoutConversionDirection.RussianToEnglish
            || string.IsNullOrEmpty(option.Replacement)
            || !IsShortTechLatinToken(option.Replacement))
        {
            return false;
        }

        return UnknownLatinNameLayoutEvaluator.TryScore(
            token,
            option.Replacement,
            isKnownRussianSourceWord: TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary),
            isNeverAutocorrectCandidate: dictionary.IsNeverAutocorrect(
                option.Replacement,
                TypingLanguage.English),
            out _)
            && (token.Equals("мущ", StringComparison.OrdinalIgnoreCase)
                || token.Equals("пзг", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsShortTechLayoutOption(ScoredCorrectionOption option)
    {
        if (option.Kind != CorrectionKind.Layout
            || option.LayoutDirection != LayoutConversionDirection.RussianToEnglish
            || string.IsNullOrEmpty(option.Replacement)
            || option.Replacement.Length is < 3 or > 4)
        {
            return false;
        }

        var hasOrdinaryVowel = false;
        foreach (var character in option.Replacement)
        {
            if (!TokenScriptAnalyzer.IsLatinLetter(character))
            {
                return false;
            }

            if (character is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U')
            {
                hasOrdinaryVowel = true;
            }
        }

        return hasOrdinaryVowel;
    }

    /// <summary>
    /// R2: weak/unknown Cyrillic typing whose physical RU→EN image is a strong English
    /// dictionary word — Russian Autocorrect must not override Layout.
    /// </summary>
    private bool IsWeakCyrillicWithStrongEnglishPhysicalImage(
        string token,
        IAutocorrectDictionary dictionary,
        out string englishImage)
    {
        englishImage = string.Empty;
        if (TokenScriptAnalyzer.ClassifyForLayout(token) != TokenScript.Cyrillic)
        {
            return false;
        }

        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary)
            || dictionary.GetFrequency(token, TypingLanguage.Russian) >= 0.70)
        {
            return false;
        }

        englishImage = _layoutConversionService.Convert(token, LayoutConversionDirection.RussianToEnglish);
        if (string.IsNullOrEmpty(englishImage)
            || string.Equals(token, englishImage, StringComparison.OrdinalIgnoreCase)
            || TokenScriptAnalyzer.Classify(englishImage) != TokenScript.Latin)
        {
            englishImage = string.Empty;
            return false;
        }

        // The layout truth-set uses the same strong-word floor as the runtime
        // dictionary policy. A higher threshold lets Russian spelling win over
        // a valid reverse-layout target in the 0.70..0.89 frequency band.
        if (!dictionary.Contains(englishImage, TypingLanguage.English)
            || dictionary.GetFrequency(englishImage, TypingLanguage.English) < 0.70)
        {
            englishImage = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Suppress English spelling when the Latin token is the layout image of a Russian typo
    /// whose unique recovery matches the layout image of the English spelling candidate
    /// (e.g. leei→lei ↔ дууш→душ). Does not suppress ordinary English typos like teh→the.
    /// </summary>
    private bool ShouldSuppressEnglishSpellingAsRussianLayoutImage(
        string token,
        string englishCandidate,
        IAutocorrectDictionary dictionary)
    {
        if (TokenScriptAnalyzer.ClassifyForLayout(token) != TokenScript.Latin)
        {
            return false;
        }

        var converted = _layoutConversionService.Convert(token, LayoutConversionDirection.EnglishToRussian);
        if (string.Equals(token, converted, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(converted))
        {
            return false;
        }

        if (TrustedWordAnalyzer.IsExactKnownOriginal(converted, dictionary))
        {
            return true;
        }

        var russianOracle = MutationClassificationOracle.Analyze(
            converted,
            TypingLanguage.Russian,
            dictionary,
            new AutocorrectionOptions());
        if (russianOracle.Class != MutationOracleClass.UniquelyRecoverable
            || string.IsNullOrEmpty(russianOracle.UniqueTarget))
        {
            return false;
        }

        // Positive layout anchors (gtie→пишу) prefer the Russian recovery over a
        // short English Unique spelling of the Latin image.
        if (LayoutCorrectionAnchors.IsPositiveAnchor(token))
        {
            return true;
        }

        var candidateConverted = _layoutConversionService.Convert(
            englishCandidate,
            LayoutConversionDirection.EnglishToRussian);
        return string.Equals(
            candidateConverted,
            russianOracle.UniqueTarget,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSuppressUnknownLayoutForStrongSpelling(
        IAutocorrectDictionary dictionary,
        WrongLayoutDetectionResult layoutDetection,
        AutocorrectionResult? sameLanguageSpelling)
    {
        if (sameLanguageSpelling is null
            || sameLanguageSpelling.Recommendation != AutocorrectionRecommendation.Candidate
            || string.IsNullOrEmpty(sameLanguageSpelling.CandidateToken)
            || sameLanguageSpelling.ConfidenceScore < AutocorrectionOptions.DefaultCandidateThreshold)
        {
            return false;
        }

        if (layoutDetection.ConversionDirection != LayoutConversionDirection.RussianToEnglish
            || string.IsNullOrEmpty(layoutDetection.CandidateToken))
        {
            return false;
        }

        if (UnknownLatinNameLayoutEvaluator.TryScore(
                layoutDetection.OriginalToken,
                layoutDetection.CandidateToken,
                isKnownRussianSourceWord: TrustedWordAnalyzer.IsExactKnownOriginal(
                    layoutDetection.OriginalToken,
                    dictionary),
                isNeverAutocorrectCandidate: dictionary.IsNeverAutocorrect(
                    layoutDetection.CandidateToken,
                    TypingLanguage.English),
                out _))
        {
            return false;
        }

        // Keep layout when the converted token is a real English word.
        return !TrustedWordAnalyzer.IsKnownWord(
            layoutDetection.CandidateToken,
            TypingLanguage.English,
            dictionary);
    }

    private static bool IsShortTechLatinToken(string token)
    {
        if (token.Length is < 3 or > 4)
        {
            return false;
        }

        var hasOrdinaryVowel = false;
        foreach (var character in token)
        {
            if (!TokenScriptAnalyzer.IsLatinLetter(character))
            {
                return false;
            }

            if (character is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U')
            {
                hasOrdinaryVowel = true;
            }
        }

        return hasOrdinaryVowel;
    }

    private static bool IsValidatedEnglishToRussianLayout(
        string token,
        ScoredCorrectionOption option,
        IAutocorrectDictionary dictionary)
    {
        if (option.LayoutDirection != LayoutConversionDirection.EnglishToRussian
            || TokenScriptAnalyzer.ClassifyForLayout(token) != TokenScript.Latin
            || dictionary.Contains(token, TypingLanguage.English)
            || TokenPlausibilityAnalyzer.ScoreEnglish(token) >= 0.45)
        {
            return false;
        }

        return dictionary.Contains(option.Replacement, TypingLanguage.Russian)
            // The compact starter lexicon stores one required everyday target
            // at 0.497... after normalization.  Permit that lower band only
            // for an explicit reverse-layout anchor; generated mutations must
            // still meet the normal strong-target floor.
            && (dictionary.GetFrequency(option.Replacement, TypingLanguage.Russian) >= 0.70
                || (LayoutCorrectionAnchors.IsPositiveAnchor(token)
                    && dictionary.GetFrequency(option.Replacement, TypingLanguage.Russian) >= 0.49))
            && !dictionary.IsNeverAutocorrect(option.Replacement, TypingLanguage.Russian);
    }

    private static int CompareOptions(ScoredCorrectionOption left, ScoredCorrectionOption right)
    {
        var scoreComparison = right.Score.CompareTo(left.Score);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        var kindComparison = GetKindPriority(right.Kind).CompareTo(GetKindPriority(left.Kind));
        if (kindComparison != 0)
        {
            return kindComparison;
        }

        return string.CompareOrdinal(left.Replacement, right.Replacement);
    }

    private static int GetKindPriority(CorrectionKind kind)
    {
        return kind switch
        {
            CorrectionKind.Combined => 4,
            CorrectionKind.Autocorrect => 3,
            CorrectionKind.Layout => 2,
            _ => 0,
        };
    }

    private readonly struct ScoredCorrectionOption
    {
        public ScoredCorrectionOption(
            string replacement,
            double score,
            CorrectionKind kind,
            LayoutConversionDirection? layoutDirection,
            KeyboardInputLanguage? targetInputLanguage)
        {
            Replacement = replacement;
            Score = score;
            Kind = kind;
            LayoutDirection = layoutDirection;
            TargetInputLanguage = targetInputLanguage;
        }

        public string Replacement { get; }

        public double Score { get; }

        public CorrectionKind Kind { get; }

        public LayoutConversionDirection? LayoutDirection { get; }

        public KeyboardInputLanguage? TargetInputLanguage { get; }
    }
}
