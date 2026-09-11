using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Engines;

/// <summary>
/// Shared production invariant: bounded candidate-set ambiguity never becomes Apply.
/// </summary>
internal static class BoundedCandidateApplyGuard
{
    private static readonly CandidateAmbiguityIndex AmbiguityIndex = CandidateAmbiguityIndex.ForStarterLexicon();

    static BoundedCandidateApplyGuard()
    {
        _ = AmbiguityIndex;
    }

    internal static bool AllowsApply(
        string token,
        string replacement,
        CorrectionKind kind,
        TypingLanguage? sourceLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        AutocorrectionOptions options,
        SentenceLanguageHint? languageHint = null)
    {
        return Evaluate(token, replacement, kind, sourceLanguage, dictionary, layoutConverter, options, languageHint)
            == BoundedApplyVerdict.Allow;
    }

    internal static BoundedApplyVerdict Evaluate(
        string token,
        string replacement,
        CorrectionKind kind,
        TypingLanguage? sourceLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        AutocorrectionOptions options,
        SentenceLanguageHint? languageHint = null)
    {
        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(replacement)
            || string.Equals(token, replacement, StringComparison.OrdinalIgnoreCase))
        {
            return BoundedApplyVerdict.NoChange;
        }

        return kind switch
        {
            CorrectionKind.Autocorrect => EvaluateSpelling(
                token,
                replacement,
                sourceLanguage,
                dictionary,
                layoutConverter,
                options),
            CorrectionKind.Layout => EvaluateDirectLayout(
                token,
                replacement,
                sourceLanguage,
                dictionary,
                layoutConverter,
                languageHint),
            CorrectionKind.Combined => EvaluateCombined(
                token,
                replacement,
                sourceLanguage,
                dictionary,
                layoutConverter,
                options),
            _ => BoundedApplyVerdict.NoChange,
        };
    }

    internal static BoundedApplyVerdict EvaluateSpelling(
        string token,
        string replacement,
        TypingLanguage? sourceLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        AutocorrectionOptions options)
    {
        if (sourceLanguage is null)
        {
            return BoundedApplyVerdict.NoChange;
        }

        // Keep the explicit common-typo safety net consistent with the final
        // production apply gate. These entries are dictionary-backed and are
        // intentionally narrower than the general one-edit oracle.
        if (CommonTypoCorrections.TryGet(
                token,
                sourceLanguage.Value,
                dictionary,
                out var commonCorrection))
        {
            return string.Equals(commonCorrection, replacement, StringComparison.OrdinalIgnoreCase)
                ? BoundedApplyVerdict.Allow
                : BoundedApplyVerdict.WaitAmbiguous;
        }

        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary)
            && !RussianOrthographyHeuristics.IsLikelyTerminalSoftSignOmission(
                token,
                sourceLanguage.Value))
        {
            return BoundedApplyVerdict.NoChange;
        }

        if (RussianOrthographyHeuristics.IsTerminalSoftSignCompletion(
                token,
                replacement,
                sourceLanguage.Value)
            && dictionary.Contains(replacement, sourceLanguage.Value)
            && dictionary.GetFrequency(replacement, sourceLanguage.Value) >= 0.70)
        {
            return BoundedApplyVerdict.Allow;
        }

        var analysis = MutationClassificationOracle.Analyze(
            token,
            sourceLanguage.Value,
            dictionary,
            options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || string.IsNullOrEmpty(analysis.UniqueTarget)
            || !string.Equals(analysis.UniqueTarget, replacement, StringComparison.OrdinalIgnoreCase)
            || OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        if (!ProductionCandidateSafetyClassifier.AllowsSpellingApply(
                token,
                replacement,
                sourceLanguage.Value,
                dictionary,
                options,
                AmbiguityIndex))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        if (ProductionCandidateSafetyClassifier.HasCrossOperationCompetition(
                token,
                replacement,
                sourceLanguage.Value,
                dictionary,
                layoutConverter,
                options))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        return BoundedApplyVerdict.Allow;
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

    internal static BoundedApplyVerdict EvaluateDirectLayout(
        string token,
        string replacement,
        TypingLanguage? sourceLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        SentenceLanguageHint? languageHint = null)
    {
        if (sourceLanguage is null)
        {
            return BoundedApplyVerdict.NoChange;
        }

        if (LayoutServiceWordWhitelist.TryGetReplacement(
                token,
                out var serviceWord,
                out var serviceDirection))
        {
            var expectedDirection = sourceLanguage.Value == TypingLanguage.English
                ? LayoutConversionDirection.EnglishToRussian
                : LayoutConversionDirection.RussianToEnglish;
            return serviceDirection == expectedDirection
                && string.Equals(serviceWord, replacement, StringComparison.OrdinalIgnoreCase)
                ? BoundedApplyVerdict.Allow
                : BoundedApplyVerdict.WaitAmbiguous;
        }

        // Evaluate exact one-to-four-key words before morphology/Bloom source
        // protection. A Bloom-only hit can be a false positive (for example a
        // wrong-layout four-key sequence), while the short policy separately
        // requires a direct target and rejects direct/user-protected sources.
        if (ShortLayoutCandidatePolicy.IsInScope(token)
            && !LayoutCorrectionAnchors.IsPositiveAnchor(token))
        {
            var shortDirection = sourceLanguage.Value == TypingLanguage.Russian
                ? LayoutConversionDirection.RussianToEnglish
                : LayoutConversionDirection.EnglishToRussian;
            var shortPhysical = layoutConverter.Convert(token, shortDirection);
            var shortSpelling = MutationClassificationOracle.Analyze(
                token,
                sourceLanguage.Value,
                dictionary,
                new AutocorrectionOptions());
            var shortTargetLanguage = sourceLanguage.Value == TypingLanguage.English
                ? TypingLanguage.Russian
                : TypingLanguage.English;
            var shortSourceIsKnown = TrustedWordAnalyzer.IsExactKnownOriginal(
                token,
                dictionary);
            var hasNoSameLanguageSpellingCandidate =
                shortSpelling.Class == MutationOracleClass.Ambiguous
                && shortSpelling.Sources.Count == 0;
            var shortPolicyAllows = shortSourceIsKnown
                ? ShortLayoutCandidatePolicy.AllowsKnownSourceInContext(
                    token,
                    replacement,
                    sourceLanguage.Value,
                    dictionary,
                    languageHint)
                : ShortLayoutCandidatePolicy.AllowsExactCandidate(
                    token,
                    replacement,
                    sourceLanguage.Value,
                    dictionary,
                    languageHint);
            var autonomousUltraFrequentTarget =
                ShortLayoutCandidatePolicy.AllowsAutonomousUltraFrequentTarget(
                    token,
                    replacement,
                    sourceLanguage.Value,
                    shortTargetLanguage,
                    dictionary);
            var ambiguousSpellingRequiresContext =
                shortSpelling.Class == MutationOracleClass.Ambiguous
                && !autonomousUltraFrequentTarget;
            if (string.Equals(shortPhysical, replacement, StringComparison.OrdinalIgnoreCase)
                && !ambiguousSpellingRequiresContext
                && (hasNoSameLanguageSpellingCandidate
                    || autonomousUltraFrequentTarget)
                && shortPolicyAllows)
            {
                return BoundedApplyVerdict.Allow;
            }
        }

        // Exact known sources must never be remapped via short-tech / layout shortcuts.
        if (TrustedWordAnalyzer.IsExactKnownOriginal(token, dictionary)
            || TrustedWordAnalyzer.ShouldBlockLayoutConversion(token, dictionary))
        {
            if (ShortLayoutCandidatePolicy.AllowsKnownSourceInContext(
                    token,
                    replacement,
                    sourceLanguage.Value,
                    dictionary,
                    languageHint))
            {
                return BoundedApplyVerdict.Allow;
            }

            if (sourceLanguage.Value == TypingLanguage.English
                && TokenScriptAnalyzer.ClassifyForLayout(token) == TokenScript.Latin)
            {
                var targetLanguageForForced = TypingLanguage.Russian;
                var forcedContextLayout =
                    dictionary.Contains(replacement, targetLanguageForForced)
                    && dictionary.GetFrequency(replacement, targetLanguageForForced) >= 0.80
                    && (LayoutCorrectionAnchors.IsPositiveAnchor(token)
                        || languageHint?.HasStrongRussian == true);

                return forcedContextLayout
                    ? BoundedApplyVerdict.Allow
                    : BoundedApplyVerdict.NoChange;
            }

            return BoundedApplyVerdict.NoChange;
        }

        // A canonical repeated-key layout alias is an explicit, bounded rule.
        // It represents the common `ррудщ` form of `hello`, which cannot be
        // recovered by the one-pass physical map alone. Keep this exception
        // narrow and require the exact canonical target.
        if (sourceLanguage.Value == TypingLanguage.Russian
            && LayoutCorrectionAnchors.TryGetCanonicalEnglishReplacement(token, out var canonicalReplacement))
        {
            return string.Equals(canonicalReplacement, replacement, StringComparison.OrdinalIgnoreCase)
                ? BoundedApplyVerdict.Allow
                : BoundedApplyVerdict.WaitAmbiguous;
        }

        var targetLanguage = sourceLanguage.Value == TypingLanguage.Russian
            ? TypingLanguage.English
            : TypingLanguage.Russian;

        var direction = sourceLanguage.Value == TypingLanguage.Russian
            ? LayoutConversionDirection.RussianToEnglish
            : LayoutConversionDirection.EnglishToRussian;

        var physical = layoutConverter.Convert(token, direction);
        if (!string.Equals(physical, replacement, StringComparison.OrdinalIgnoreCase))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        // Short technical targets need a deterministic second signal. Their
        // Cyrillic physical images can also look like ordinary Russian typos,
        // so the generic mutation oracle is deliberately conservative. The
        // bounded technical vocabulary is still exact-map-only and never runs
        // for known/protected source words.
        var isShortTechnicalLayoutTarget =
            sourceLanguage.Value == TypingLanguage.Russian
            && targetLanguage == TypingLanguage.English
            && replacement.Length is >= 3 and <= 4
            && LayoutTechnicalVocabulary.Contains(replacement)
            && dictionary.Contains(replacement, TypingLanguage.English)
            && dictionary.GetFrequency(replacement, TypingLanguage.English) >= 0.80
            && !dictionary.IsNeverAutocorrect(replacement, TypingLanguage.English)
            && TokenPlausibilityAnalyzer.ScoreEnglish(replacement) >= 0.78
            && TokenPlausibilityAnalyzer.ScoreRussian(token) < 0.90;
        if (isShortTechnicalLayoutTarget)
        {
            return BoundedApplyVerdict.Allow;
        }

        // Compute same-language spelling competition before any reverse-layout
        // shortcut. Otherwise an unknown Latin mutation whose physical image
        // is a frequent Russian word can bypass the ambiguity gate and be
        // applied as layout even when the token also has a credible spelling
        // explanation. The explicit anchor list remains the only intentional
        // exception.
        var spelling = MutationClassificationOracle.Analyze(
            token,
            sourceLanguage.Value,
            dictionary,
            new AutocorrectionOptions());

        // Preserve the deterministic reverse-layout path for an unknown Latin
        // token whose exact physical image is a known Russian word.  This is
        // deliberately narrower than the old generic strong-image exception:
        // it requires an unknown source, a real RU lexicon hit, a non-
        // protected target and an explicit reverse-layout anchor.  The lower
        // frequency band is reserved for those anchors; arbitrary generated
        // reverse-layout mutations continue through the normal >=0.70 path.
        var validatedEnglishToRussianLayout =
            sourceLanguage.Value == TypingLanguage.English
            && targetLanguage == TypingLanguage.Russian
            && TokenScriptAnalyzer.ClassifyForLayout(token) == TokenScript.Latin
            && LayoutCorrectionAnchors.IsPositiveAnchor(token)
            && !dictionary.Contains(token, TypingLanguage.English)
            && dictionary.Contains(replacement, TypingLanguage.Russian)
            && dictionary.GetFrequency(replacement, TypingLanguage.Russian) >= 0.49
            && dictionary.GetFrequency(replacement, TypingLanguage.Russian) < 0.70
            && !dictionary.IsNeverAutocorrect(replacement, TypingLanguage.Russian);

        if (validatedEnglishToRussianLayout)
        {
            return BoundedApplyVerdict.Allow;
        }

        // Do not let the early physical-image shortcuts bypass an ambiguous
        // same-language spelling explanation.  This is the important ordering
        // rule for generated and live mutations: a strong layout image is useful,
        // but an existing credible spelling competition must still fail closed.
        // Explicit layout anchors are the only deterministic exception.
        if (spelling.Class == MutationOracleClass.Ambiguous
            && spelling.Sources.Count >= 1
            && !LayoutCorrectionAnchors.IsPositiveAnchor(token)
            && !validatedEnglishToRussianLayout)
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        // Short Latin RU→EN is allowed only via the validated unknown-name path
        // (мущ→veo, пзг→gpu). Arbitrary vowel-containing Latin is never enough.
        var unknownNameValidated = false;
        if (sourceLanguage.Value == TypingLanguage.Russian
            && targetLanguage == TypingLanguage.English
            && IsShortTechLatinReplacement(replacement, sourceLanguage.Value))
        {
            unknownNameValidated = UnknownLatinNameLayoutEvaluator.TryScore(
                token,
                replacement,
                isKnownRussianSourceWord: false,
                isNeverAutocorrectCandidate: dictionary.IsNeverAutocorrect(
                    replacement,
                    TypingLanguage.English),
                out _);
            if (!unknownNameValidated)
            {
                return BoundedApplyVerdict.WaitAmbiguous;
            }
        }

        // Symmetric direct-layout rule.  If an unknown Latin token is a full
        // physical English→Russian image of an exact Russian word, it is a
        // wrong-layout error even when it is short: dhjlt→вроде, dct→все,
        // jrtq→окей.  Exact English sources, user-protected entries, URLs and
        // identifiers have already returned above, so this does not turn real
        // English text into Russian by accident.
        if (sourceLanguage.Value == TypingLanguage.English
            && targetLanguage == TypingLanguage.Russian
            && !dictionary.Contains(token, TypingLanguage.English)
            // Long unknown Latin strings are commonly identifiers, hashes or
            // accidental key noise.  Do not turn them into a Russian word
            // solely because the physical image happens to be in the lexicon;
            // the short reverse-layout path (e.g. ybxtuj→ничего) remains.
            && token.Length <= 8
            && TokenPlausibilityAnalyzer.ScoreEnglish(token) < 0.85
            // Require a real starter-lexicon entry for this symmetric
            // unknown-English path.  Bloom/form-only membership is useful for
            // protecting an existing Russian word, but is too broad here:
            // long physical gibberish such as plhfdcndeqnt can otherwise be
            // promoted to a rare form ("здравствуйте") without enough live
            // evidence.  The ordinary exact words needed for reverse layout
            // (вроде, все, окей, привет) are all present in the lexicon.
            && dictionary.Contains(replacement, TypingLanguage.Russian)
            // A starter-lexicon hit alone is not enough for an unknown Latin token:
            // rare Russian words are also valid physical images of random English
            // input. Keep this reverse-layout shortcut conservative and require
            // frequent-target evidence, as the normal layout path does.
            && dictionary.GetFrequency(replacement, TypingLanguage.Russian) >= 0.70
            && !dictionary.IsNeverAutocorrect(replacement, TypingLanguage.Russian)
            && (LayoutCorrectionAnchors.IsPositiveAnchor(token)
                || spelling.Class is not (
                    MutationOracleClass.Ambiguous
                    or MutationOracleClass.UniquelyRecoverable)
                || validatedEnglishToRussianLayout))
        {
            return BoundedApplyVerdict.Allow;
        }

        // MustPreserve: strong known source never remaps via layout (except forced
        // English-context anchors handled above). Checked before empty-Ambiguous Allow.
        var sourceStrong = dictionary.Contains(token, sourceLanguage.Value)
            && dictionary.GetFrequency(token, sourceLanguage.Value) >= 0.70;
        if (sourceStrong && !IsRequiredUnknownNameAnchor(token))
        {
            return BoundedApplyVerdict.NoChange;
        }

        // A complete physical RU→EN conversion to an exact, frequent English
        // word is the normal live wrong-layout case: вуфк→dear, рш→hi,
        // цфше→wait, цщкл→work.  Exact Russian sources have already returned
        // above, so a real word is still safe.  For an unknown Russian-looking
        // token, the physical keyboard mapping is more useful evidence than a
        // coincidental one-edit Russian candidate such as цщкл→цикл.
        var isFrequentKnownPhysicalEnglish =
            sourceLanguage.Value == TypingLanguage.Russian
            && targetLanguage == TypingLanguage.English
            && dictionary.Contains(replacement, TypingLanguage.English)
            && dictionary.GetFrequency(replacement, TypingLanguage.English) >= 0.70
            && (LayoutCorrectionAnchors.IsPositiveAnchor(token)
                || spelling.Class is not (
                    MutationOracleClass.Ambiguous
                    or MutationOracleClass.UniquelyRecoverable));
        if (isFrequentKnownPhysicalEnglish)
        {
            return BoundedApplyVerdict.Allow;
        }

        // A valid target can be absent from the compact frequency list even
        // though the physical conversion is unambiguous. Technical compounds
        // such as "frontend" and "backend" are common examples. If the target
        // is a strong lexicon word, the source has no same-language spelling
        // competitor, and the target word shape is clearly valid, allow the
        // direct layout operation. Protected tokens, known sources, short tech
        // aliases, and spelling competitors have already been handled above.
        var isStrongUnknownPhysicalEnglish =
            sourceLanguage.Value == TypingLanguage.Russian
            && targetLanguage == TypingLanguage.English
            && replacement.Length >= 3
            && dictionary.Contains(replacement, TypingLanguage.English)
            && dictionary.GetFrequency(replacement, TypingLanguage.English) >= 0.80
            && !dictionary.IsNeverAutocorrect(replacement, TypingLanguage.English)
            && TokenPlausibilityAnalyzer.ScoreEnglish(replacement) >= 0.82
            && (TokenPlausibilityAnalyzer.ScoreRussian(token) < 0.90
                || unknownNameValidated)
            && spelling.Sources.Count == 0
            && spelling.Class == MutationOracleClass.Ambiguous;
        if (isStrongUnknownPhysicalEnglish)
        {
            return BoundedApplyVerdict.Allow;
        }

        // Validated unknown-name RU→EN is restricted to the required anchors
        // (мущ→veo, пзг→gpu). Other short Latin layouts must pass the full
        // Unique/Ambiguous competition checks below.
        if (unknownNameValidated && IsRequiredUnknownNameAnchor(token))
        {
            return BoundedApplyVerdict.Allow;
        }

        if (spelling.Class == MutationOracleClass.UniquelyRecoverable
            && !string.IsNullOrEmpty(spelling.UniqueTarget)
            && !string.Equals(spelling.UniqueTarget, replacement, StringComparison.OrdinalIgnoreCase))
        {
            var uniqueScript = TokenScriptAnalyzer.Classify(spelling.UniqueTarget);
            var replacementScript = TokenScriptAnalyzer.Classify(replacement);

            // Same-script Unique always blocks a conflicting Apply.
            if (uniqueScript == replacementScript)
            {
                return BoundedApplyVerdict.WaitAmbiguous;
            }

            // Cyrillic Unique spelling must beat non-validated Latin layout.
            if (TokenScriptAnalyzer.ClassifyForLayout(token) == TokenScript.Cyrillic
                && uniqueScript == TokenScript.Cyrillic
                && replacementScript == TokenScript.Latin)
            {
                return BoundedApplyVerdict.WaitAmbiguous;
            }

            // Latin Unique spelling must beat non-anchor Russian layout.
            if (TokenScriptAnalyzer.ClassifyForLayout(token) == TokenScript.Latin
                && uniqueScript == TokenScript.Latin
                && replacementScript == TokenScript.Cyrillic
                && !LayoutCorrectionAnchors.IsPositiveAnchor(token))
            {
                return BoundedApplyVerdict.WaitAmbiguous;
            }
        }

        if (spelling.Class == MutationOracleClass.UniquelyRecoverable
            && OperationPrecisionGate.HasDiscardedCredibleCompetitor(spelling))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        // Empty-source Ambiguous: only required unknown-name anchors or positive
        // layout anchors may Apply. A strong physical image by itself is not
        // enough here: generated mutations can accidentally map to a frequent
        // word in the other script, and the safe result is Wait.
        if (spelling.Class == MutationOracleClass.Ambiguous
            && spelling.Sources.Count == 0
            && !LayoutCorrectionAnchors.IsPositiveAnchor(token)
            && !IsRequiredUnknownNameAnchor(token)
            && !validatedEnglishToRussianLayout)
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        if (spelling.Class == MutationOracleClass.Ambiguous
            && spelling.Sources.Count >= 1
            && !LayoutCorrectionAnchors.IsPositiveAnchor(token)
            && !validatedEnglishToRussianLayout)
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        // A short unknown Latin target is not enough evidence on its own. A
        // short *known, frequent* English word is different: it is the normal
        // wrong-layout case (`вуфк`→`dear`, `цфше`→`wait`, `цщкл`→`work`).
        // The spelling competition checks above have already protected a
        // plausible Russian explanation, and exact Russian sources returned
        // earlier, so keep the generic short-tech block only for unknown names.
        if (sourceLanguage.Value == TypingLanguage.Russian
            && targetLanguage == TypingLanguage.English
            && IsShortTechLatinReplacement(replacement, sourceLanguage.Value)
            && !dictionary.Contains(replacement, TypingLanguage.English))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        var targetStrong = validatedEnglishToRussianLayout
            || (dictionary.Contains(replacement, targetLanguage)
                && dictionary.GetFrequency(replacement, targetLanguage) >= 0.70);
        if (!targetStrong)
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        if (dictionary.Contains(token, targetLanguage))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        if (HasCompetingLayoutTargets(
                token,
                replacement,
                sourceLanguage.Value,
                targetLanguage,
                dictionary,
                layoutConverter))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        return BoundedApplyVerdict.Allow;
    }

    private static bool IsRequiredUnknownNameAnchor(string token)
    {
        return token.Equals("мущ", StringComparison.OrdinalIgnoreCase)
            || token.Equals("пзг", StringComparison.OrdinalIgnoreCase);
    }

    internal static BoundedApplyVerdict EvaluateCombined(
        string token,
        string replacement,
        TypingLanguage? sourceLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        AutocorrectionOptions options)
    {
        if (sourceLanguage is null)
        {
            return BoundedApplyVerdict.NoChange;
        }

        var direction = sourceLanguage.Value == TypingLanguage.Russian
            ? LayoutConversionDirection.RussianToEnglish
            : LayoutConversionDirection.EnglishToRussian;

        var converted = layoutConverter.Convert(token, direction);
        if (string.Equals(converted, token, StringComparison.Ordinal))
        {
            return BoundedApplyVerdict.NoChange;
        }

        var targetLanguage = sourceLanguage.Value == TypingLanguage.Russian
            ? TypingLanguage.English
            : TypingLanguage.Russian;

        if (string.Equals(converted, replacement, StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateDirectLayout(
                token,
                replacement,
                sourceLanguage,
                dictionary,
                layoutConverter);
        }

        if (TrustedWordAnalyzer.ShouldBlockLayoutConversion(token, dictionary))
        {
            return BoundedApplyVerdict.NoChange;
        }

        if (!LayoutCorrectionAnchors.IsPositiveAnchor(token)
            && TrustedWordAnalyzer.IsExactKnownOriginal(replacement, dictionary))
        {
            return BoundedApplyVerdict.WaitAmbiguous;
        }

        return EvaluateSpelling(converted, replacement, targetLanguage, dictionary, layoutConverter, options);
    }

    internal static bool AllowsPreparedLayout(
        string token,
        string replacement,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter,
        SentenceLanguageHint? languageHint = null)
    {
        if (LayoutServiceWordWhitelist.TryGetCommaPrefixedRussianReplacement(
                token,
                out var commaPrefixedReplacement))
        {
            return string.Equals(
                commaPrefixedReplacement,
                replacement,
                StringComparison.OrdinalIgnoreCase);
        }

        var script = TokenScriptAnalyzer.ClassifyForLayout(token);
        var sourceLanguage = script switch
        {
            TokenScript.Latin => TypingLanguage.English,
            TokenScript.Cyrillic => TypingLanguage.Russian,
            _ => (TypingLanguage?)null,
        };

        return EvaluateDirectLayout(
                token,
                replacement,
                sourceLanguage,
                dictionary,
                layoutConverter,
                languageHint)
            == BoundedApplyVerdict.Allow;
    }

    private static bool HasCompetingLayoutTargets(
        string token,
        string replacement,
        TypingLanguage sourceLanguage,
        TypingLanguage targetLanguage,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService layoutConverter)
    {
        var direction = sourceLanguage == TypingLanguage.Russian
            ? LayoutConversionDirection.RussianToEnglish
            : LayoutConversionDirection.EnglishToRussian;

        var physical = layoutConverter.Convert(token, direction);
        if (!string.Equals(physical, replacement, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var winnerFrequency = dictionary.GetFrequency(replacement, targetLanguage);
        var layoutCompetitors = AmbiguityIndex.FindComparableKnownTargets(token, sourceLanguage, 0.50)
            .Where(neighbor => layoutConverter.Convert(neighbor.Word, direction)
                is var mapped
                && string.Equals(mapped, replacement, StringComparison.OrdinalIgnoreCase))
            .Where(neighbor => !string.Equals(neighbor.Word, token, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (layoutCompetitors.Count == 0)
        {
            return false;
        }

        var strongest = layoutCompetitors
            .OrderByDescending(static neighbor => neighbor.Frequency)
            .First();
        return strongest.Frequency >= winnerFrequency - 0.12;
    }

    private static bool IsShortTechLatinReplacement(string replacement, TypingLanguage sourceLanguage)
    {
        if (sourceLanguage != TypingLanguage.Russian
            || replacement.Length is < 3 or > 4)
        {
            return false;
        }

        var hasOrdinaryVowel = false;
        foreach (var character in replacement)
        {
            if (character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z'))
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
}

internal enum BoundedApplyVerdict
{
    Allow,
    WaitAmbiguous,
    NoChange,
}
