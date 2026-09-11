using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Applies the same conservative boundary rules to candidates supplied by a
/// local spelling engine. This class is deliberately separate from the live
/// pipeline: callers must opt in explicitly and still own policy/replacement.
/// </summary>
public interface IExternalAutocorrectionEvaluator
{
    AutocorrectionResult Evaluate(
        string token,
        TypingLanguage activeLanguage,
        IAutocorrectDictionary dictionary,
        IExternalSpellCorrectionProvider provider,
        AutocorrectionOptions? options = null);
}

public sealed class ExternalAutocorrectionEvaluator : IExternalAutocorrectionEvaluator
{
    public AutocorrectionResult Evaluate(
        string token,
        TypingLanguage activeLanguage,
        IAutocorrectDictionary dictionary,
        IExternalSpellCorrectionProvider provider,
        AutocorrectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(provider);

        options ??= new AutocorrectionOptions();
        if (string.IsNullOrWhiteSpace(token)
            || ProtectedTokenAnalyzer.IsProtected(token)
            || !IsLanguageSupported(token, activeLanguage)
            || token.Length < options.MinCandidateTokenLength)
        {
            return Build(token, activeLanguage, null, 0.0, AutocorrectionRecommendation.NoChange);
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(token);
        if (!RussianOrthographyHeuristics.ShouldPreserveExactWord(normalized, activeLanguage, dictionary)
            && RussianOrthographyHeuristics.TryGetLowFrequencyTerminalSoftSignCorrection(
                normalized,
                activeLanguage,
                dictionary,
                out var terminalSoftSignCorrection))
        {
            return Build(
                token,
                activeLanguage,
                CapitalizationPreserver.Apply(token, terminalSoftSignCorrection),
                0.94,
                AutocorrectionRecommendation.Candidate);
        }

        if (dictionary.IsNeverAutocorrect(normalized, activeLanguage)
            || RussianOrthographyHeuristics.ShouldPreserveExactWord(normalized, activeLanguage, dictionary))
        {
            return Build(token, activeLanguage, null, 0.0, AutocorrectionRecommendation.NoChange);
        }

        if (CommonTypoCorrections.TryGet(normalized, activeLanguage, dictionary, out var commonCorrection))
        {
            return Build(
                token,
                activeLanguage,
                CapitalizationPreserver.Apply(token, commonCorrection),
                0.98,
                AutocorrectionRecommendation.Candidate);
        }

        // Apply only a deterministic orthography transform when the runtime
        // dictionary confirms exactly one strong target. This covers general
        // classes such as жи/ши, ча/ща, чу/щу, vowel confusion and terminal
        // soft-sign omissions without turning the stress corpus into a word
        // table. Ambiguous variants continue through the normal conservative
        // provider ranking below.
        if (RussianOrthographyHeuristics.TryGetDeterministicCorrection(
                normalized,
                activeLanguage,
                dictionary,
                out var deterministicCorrection))
        {
            return Build(
                token,
                activeLanguage,
                CapitalizationPreserver.Apply(token, deterministicCorrection),
                0.94,
                AutocorrectionRecommendation.Candidate);
        }

        // A forbidden жи/ши/ча/ща/чу/щу digraph is itself a strong signal.
        // If no dictionary-backed deterministic form exists, do not let a
        // frequency-heavy unrelated word (for example a different lexical
        // neighbour) win automatically.
        if (activeLanguage == TypingLanguage.Russian
            && RussianOrthographyHeuristics.HasForbiddenRussianDigraph(normalized))
        {
            return Build(token, activeLanguage, null, 0.0, AutocorrectionRecommendation.Wait);
        }

        // If the local dictionary contains two equally near vowel variants,
        // the token needs context or an explicit user choice. This hard stop
        // prevents a frequency tie-break from turning a plausible typo into
        // a confident but unrelated word.
        if (activeLanguage == TypingLanguage.Russian
            && RussianOrthographyHeuristics.HasAmbiguousNearestVowelCorrection(
                normalized,
                activeLanguage,
                dictionary))
        {
            return Build(token, activeLanguage, null, 0.0, AutocorrectionRecommendation.Wait);
        }

        var maxExternalEditDistance = Math.Clamp(
            options.MaxExternalEditDistance,
            min: 1,
            max: 2);
        var candidates = provider.FindCandidates(
                normalized,
                activeLanguage,
                maxEditDistance: maxExternalEditDistance)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Word))
            .Select(candidate => candidate with
            {
                // Hunspell supplies morphology but no corpus count. Use the
                // shared dictionary's frequency sidecar for those candidates.
                Frequency = candidate.Frequency > 0
                    ? candidate.Frequency
                    : ToFrequencyCount(dictionary.GetFrequency(candidate.Word, activeLanguage)),
            })
            .Where(candidate => candidate.EditDistance > 0
                && candidate.EditDistance <= maxExternalEditDistance)
            .Where(candidate => !string.Equals(
                AutocorrectDictionaryNormalizer.NormalizeLookupKey(candidate.Word),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .Where(candidate => IsLanguageSupported(candidate.Word, activeLanguage))
            .Where(candidate => !ProtectedTokenAnalyzer.IsProtected(candidate.Word))
            .GroupBy(candidate => AutocorrectDictionaryNormalizer.NormalizeLookupKey(candidate.Word), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => candidate.EditDistance)
                .ThenByDescending(candidate => candidate.Frequency)
                .First())
            .OrderBy(candidate => candidate.EditDistance)
            .ThenByDescending(candidate => candidate.Frequency)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            return Build(token, activeLanguage, null, 0.0, AutocorrectionRecommendation.NoChange);
        }

        var bestDistance = candidates[0].EditDistance;
        var nearest = candidates.Where(candidate => candidate.EditDistance == bestDistance).ToArray();
        var allNearest = nearest;

        // Hunspell has no corpus frequencies and reports zero for its
        // suggestions. When SymSpell (or another ranked provider) supplied a
        // scored candidate, those unscored fallbacks must not make an
        // otherwise clear result ambiguous. They remain eligible when no
        // scored candidate exists, which keeps Hunspell-only dictionaries
        // useful for rare words.
        if (nearest.Any(candidate => candidate.Frequency > 0))
        {
            nearest = nearest
                .Where(candidate => candidate.Frequency > 0)
                .ToArray();
        }

        // Short Cyrillic tokens often have a real word at both sides of a
        // transposition (for example `дла` can be `дал`, `для` or `дела`).
        // Without sentence context, applying the transposition would be a
        // confident but arbitrary rewrite. Keep it as Wait; longer and
        // English transpositions such as `teh`/`adn` retain the normal rule.
        if (nearest.Any(candidate => GetEditOperationRank(normalized, candidate.Word) == 0)
            && TokenScriptAnalyzer.Classify(normalized) == TokenScript.Cyrillic
            && normalized.Length <= 4
            && HasCompetingEditShape(allNearest, normalized))
        {
            return Build(token, activeLanguage, null, 0.0, AutocorrectionRecommendation.Wait);
        }

        // Do not discard a high-frequency substitution merely because an
        // unrelated deletion/insertion has a preferred operation shape. This
        // was the source of `мошина`→`мошна` and `рвбота`→`рвота`.
        var orderedNearest = nearest
            .OrderByDescending(candidate => RussianOrthographyHeuristics.IsTerminalSoftSignCompletion(
                normalized,
                candidate.Word,
                activeLanguage))
            .ThenByDescending(candidate => candidate.Frequency)
            .ThenBy(candidate => GetEditOperationRank(normalized, candidate.Word))
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var frequencyOrdered = orderedNearest
            .OrderByDescending(candidate => candidate.Frequency)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (TrySelectHighConfidenceWinner(normalized, frequencyOrdered, out var highConfidenceWinner))
        {
            var highConfidenceScore = Score(normalized, highConfidenceWinner, orderedNearest.Length);
            return Build(
                token,
                activeLanguage,
                CapitalizationPreserver.Apply(token, highConfidenceWinner.Word),
                highConfidenceScore,
                AutocorrectionRecommendation.Candidate);
        }

        if (orderedNearest.Length > 1
            && !RussianOrthographyHeuristics.IsTerminalSoftSignCompletion(
                normalized,
                orderedNearest[0].Word,
                activeLanguage)
            && !HasDecisiveFrequencyWinner(orderedNearest, normalized))
        {
            return Build(token, activeLanguage, null, 0.0, AutocorrectionRecommendation.Wait);
        }

        var winner = orderedNearest[0];
        if (activeLanguage == TypingLanguage.Russian
            && normalized.Length >= 8
            && winner.EditDistance == 1
            && winner.Word.Length == normalized.Length - 1
            && winner.Frequency < 900_000
            && !IsRepeatedCharacterReduction(normalized, winner.Word))
        {
            // A long deletion can be a valid, frequent word rather than the
            // intended spelling. Keep lower-frequency deletions conservative;
            // a high-frequency dictionary target is strong enough evidence for
            // an unrecognised one-character typo such as an extra vowel.
            return Build(token, activeLanguage, null, confidence: 0.0, AutocorrectionRecommendation.Wait);
        }

        var score = Score(normalized, winner, orderedNearest.Length);
        if (winner.EditDistance > 1 && winner.Frequency < 900_000)
        {
            return Build(token, activeLanguage, null, score, AutocorrectionRecommendation.Wait);
        }

        if (score < options.CandidateThreshold)
        {
            return Build(token, activeLanguage, null, score, AutocorrectionRecommendation.Wait);
        }

        var corrected = CapitalizationPreserver.Apply(token, winner.Word);
        return Build(token, activeLanguage, corrected, score, AutocorrectionRecommendation.Candidate);
    }

    private static bool HasDecisiveFrequencyWinner(
        IReadOnlyList<ExternalSpellCandidate> nearest,
        string source)
    {
        if (nearest.Count < 2 || nearest[0].Frequency <= 0)
        {
            return false;
        }

        var ordered = nearest
            .OrderByDescending(candidate => RussianOrthographyHeuristics.IsTerminalSoftSignCompletion(
                source,
                candidate.Word,
                TypingLanguage.Russian))
            .ThenByDescending(candidate => candidate.Frequency)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (RussianOrthographyHeuristics.IsTerminalSoftSignCompletion(
                source,
                ordered[0].Word,
                TypingLanguage.Russian)
            && ordered[0].Frequency >= 700_000
            && ordered[0].Frequency + 30_000 >= ordered[1].Frequency)
        {
            return true;
        }

        // Prefer completing a missing character over deleting a character
        // from the source when both candidates are otherwise close. A shorter
        // valid word is a common false positive for a genuine omission.
        if (IsInsertionCandidate(source, ordered[0].Word)
            && IsDeletionCandidate(source, ordered[1].Word)
            && ordered[0].Frequency >= 800_000
            && ordered[0].Frequency + 100_000 >= ordered[1].Frequency)
        {
            return true;
        }

        var language = TokenScriptAnalyzer.Classify(source) == TokenScript.Cyrillic
            ? TypingLanguage.Russian
            : TypingLanguage.English;
        var detailedOperation = EditOperationClassifier.Classify(
            source,
            ordered[0].Word,
            language);
        if (ordered[0].Frequency >= 900_000
            && ordered[0].Frequency >= ordered[1].Frequency + 120_000
            && detailedOperation is EditOperationType.AdjacentKeySubstitution
                or EditOperationType.VowelSubstitution)
        {
            return true;
        }

        if (detailedOperation == EditOperationType.VowelSubstitution
            && ordered[0].Frequency >= 970_000
            && ordered[0].Frequency >= ordered[1].Frequency + 50_000)
        {
            return true;
        }

        if (ordered[0].Frequency >= 970_000
            && ordered[0].Frequency >= ordered[1].Frequency + 50_000
            && IsSingleRussianVowelSubstitution(source, ordered[0].Word))
        {
            return true;
        }

        // A one-edit shape is useful evidence in its own right. The relaxed
        // margins below cover common transpositions (`teh`/`adn`) and clear
        // extra/missing-character typos (`мирр`/`helo`) while retaining a
        // conservative high-frequency floor for genuinely ambiguous words.
        var operation = GetEditOperationRank(source, ordered[0].Word);
        var ratio = (double)ordered[0].Frequency / ordered[1].Frequency;
        return operation == 0
            ? ordered[0].Frequency >= 980_000 && ratio >= 1.015
            : operation == 1
                && ordered[0].Frequency >= 850_000
                && ratio >= 1.10;
    }

    private static bool TrySelectHighConfidenceWinner(
        string source,
        IReadOnlyList<ExternalSpellCandidate> ordered,
        out ExternalSpellCandidate winner)
    {
        winner = null!;
        if (ordered.Count < 2)
        {
            return false;
        }

        if (TokenScriptAnalyzer.Classify(source) == TokenScript.Cyrillic
            && source.Length <= 3)
        {
            return false;
        }

        var best = ordered[0];
        var second = ordered[1];
        if (best.Frequency < 950_000
            || best.Frequency < second.Frequency + 50_000
            || best.EditDistance > 1)
        {
            return false;
        }

        var isSingleVowel = IsSingleRussianVowelSubstitution(source, best.Word);
        var isInsertionAgainstShorter = IsInsertionCandidate(source, best.Word)
            && (IsDeletionCandidate(source, second.Word)
                || second.Frequency <= best.Frequency - 150_000);
        var isDeletionAgainstLonger = IsDeletionCandidate(source, best.Word)
            && second.Frequency <= best.Frequency - 150_000;
        var isClearOneEdit = isSingleVowel
            || isInsertionAgainstShorter
            || isDeletionAgainstLonger
            || (best.Frequency >= second.Frequency + 150_000
                && GetEditOperationRank(source, best.Word) is 0 or 2);

        if (!isClearOneEdit)
        {
            return false;
        }

        winner = best;
        return true;
    }

    private static double Score(
        string source,
        ExternalSpellCandidate candidate,
        int nearestCount)
    {
        var frequencyBonus = Math.Clamp(candidate.Frequency / 1_000_000.0, 0.0, 1.0) * 0.12;
        var ambiguityPenalty = nearestCount > 1 ? 0.08 : 0.0;
        var distancePenalty = candidate.EditDistance > 1
            ? 0.10 * (candidate.EditDistance - 1)
            : 0.0;
        var softSignBonus = RussianOrthographyHeuristics.IsTerminalSoftSignCompletion(
                source,
                candidate.Word,
                TypingLanguage.Russian)
            ? 0.04
            : 0.0;
        return Math.Clamp(
            0.76 + frequencyBonus - ambiguityPenalty - distancePenalty + softSignBonus,
            0.0,
            0.99);
    }

    private static long ToFrequencyCount(double frequency)
        => frequency <= 0.0
            ? 0L
            : (long)Math.Round(Math.Clamp(frequency, 0.0, 1.0) * 1_000_000.0);

    private static bool IsInsertionCandidate(string source, string candidate)
        => candidate.Length == source.Length + 1;

    private static bool IsDeletionCandidate(string source, string candidate)
        => candidate.Length == source.Length - 1;

    private static bool IsRepeatedCharacterReduction(string source, string candidate)
    {
        if (source.Length != candidate.Length + 1)
        {
            return false;
        }

        for (var index = 0; index + 1 < source.Length; index++)
        {
            if (source[index] != source[index + 1]
                || !string.Equals(source.Remove(index, 1), candidate, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsSingleRussianVowelSubstitution(string source, string candidate)
    {
        const string vowels = "аеёиоуыэюя";
        if (source.Length != candidate.Length)
        {
            return false;
        }

        var differences = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == candidate[index])
            {
                continue;
            }

            differences++;
            if (differences > 1
                || !vowels.Contains(source[index], StringComparison.OrdinalIgnoreCase)
                || !vowels.Contains(candidate[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return differences == 1;
    }

    private static int GetEditOperationRank(string source, string candidate)
    {
        if (source.Length == candidate.Length + 1)
        {
            return 1; // accidental extra character in the source
        }

        if (candidate.Length == source.Length + 1)
        {
            return 1; // accidental missing character in the source
        }

        if (source.Length == candidate.Length && IsAdjacentTransposition(source, candidate))
        {
            return 0;
        }

        return 2; // substitution (or an unusual provider result)
    }

    private static bool IsAdjacentTransposition(string source, string candidate)
    {
        var mismatches = new List<int>(2);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != candidate[index])
            {
                mismatches.Add(index);
            }
        }

        return mismatches.Count == 2
            && mismatches[1] == mismatches[0] + 1
            && source[mismatches[0]] == candidate[mismatches[1]]
            && source[mismatches[1]] == candidate[mismatches[0]];
    }

    private static bool HasCompetingEditShape(
        IReadOnlyList<ExternalSpellCandidate> candidates,
        string source)
    {
        if (candidates.Count < 2)
        {
            return false;
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Frequency)
            .ToArray();
        var best = ordered[0];
        var bestRank = GetEditOperationRank(source, best.Word);
        return ordered.Skip(1).Any(candidate =>
            GetEditOperationRank(source, candidate.Word) != bestRank
            && candidate.Frequency >= best.Frequency * 0.85);
    }

    private static bool IsLanguageSupported(string token, TypingLanguage language)
    {
        return language switch
        {
            TypingLanguage.English => TokenScriptAnalyzer.Classify(token) == TokenScript.Latin,
            TypingLanguage.Russian => TokenScriptAnalyzer.Classify(token) == TokenScript.Cyrillic,
            _ => false,
        };
    }

    private static AutocorrectionResult Build(
        string original,
        TypingLanguage language,
        string? candidate,
        double confidence,
        AutocorrectionRecommendation recommendation)
    {
        return new AutocorrectionResult
        {
            OriginalToken = original,
            CandidateToken = candidate,
            ConfidenceScore = confidence,
            IsExternalProviderCandidate = recommendation == AutocorrectionRecommendation.Candidate,
            Recommendation = recommendation,
            ActiveLanguage = language,
        };
    }
}
