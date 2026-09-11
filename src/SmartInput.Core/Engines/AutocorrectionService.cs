using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Engines;

public interface IAutocorrectionService
{
    AutocorrectionResult Evaluate(
        string token,
        TypingLanguage activeLanguage,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null);
}

public sealed class AutocorrectionService : IAutocorrectionService
{
    private static readonly CandidateAmbiguityIndex SharedIndex = CandidateAmbiguityIndex.ForStarterLexicon();

    private readonly CandidateAmbiguityIndex _ambiguityIndex;
    private readonly IExternalAutocorrectionEvaluator? _externalEvaluator;
    private readonly IExternalSpellCorrectionProvider? _externalProvider;
    private readonly ISettingsService? _settingsService;

    static AutocorrectionService()
    {
        // Force lexicon reverse-index construction during type initialization so
        // the first live keystroke does not pay a multi-second cold-start cost.
        _ = SharedIndex;
    }

    public AutocorrectionService(
        CandidateAmbiguityIndex? ambiguityIndex = null,
        IExternalAutocorrectionEvaluator? externalEvaluator = null,
        IExternalSpellCorrectionProvider? externalProvider = null,
        ISettingsService? settingsService = null)
    {
        _ambiguityIndex = ambiguityIndex ?? SharedIndex;
        _externalEvaluator = externalEvaluator;
        _externalProvider = externalProvider;
        _settingsService = settingsService;
    }

    public AutocorrectionResult Evaluate(
        string token,
        TypingLanguage activeLanguage,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(dictionary);

        options ??= new AutocorrectionOptions();

        // Perform the cheap, deterministic guards before consulting an
        // external provider.  Providers may legitimately return Wait when
        // they see several equally plausible forms.  That must not hide a
        // dictionary-backed high-confidence correction that the core engine
        // already knows how to apply (for example a bounded omission pattern).
        if (string.IsNullOrWhiteSpace(token))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        if (ProtectedTokenAnalyzer.IsProtected(token))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        var normalized = token.ToLowerInvariant();
        if (dictionary.IsNeverAutocorrect(normalized, activeLanguage))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        if (!IsLanguageSupported(token, activeLanguage))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        if (token.Length < options.MinCandidateTokenLength)
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.Wait);
        }

        // Never rewrite an exact word merely because a correction fallback or
        // an external provider happens to rank another form higher.
        if (dictionary.Contains(normalized, activeLanguage)
            && !RussianOrthographyHeuristics.IsLikelyTerminalSoftSignOmission(normalized, activeLanguage)
            && !RussianOrthographyHeuristics.TryGetLowFrequencyTerminalSoftSignCorrection(
                normalized,
                activeLanguage,
                dictionary,
                out _)
            && !RussianOrthographyHeuristics.IsLowFrequencyRussianSpellingProbe(
                normalized,
                activeLanguage,
                dictionary))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        // This check is deliberately before external-provider ambiguity.  It
        // is a bounded, dictionary-backed rule, not a growing per-word list.
        // A provider's conservative Wait must not suppress it.
        if (CommonTypoCorrections.TryGet(normalized, activeLanguage, dictionary, out var commonCorrection))
        {
            return BuildResult(
                token,
                CapitalizationPreserver.Apply(token, commonCorrection),
                activeLanguage,
                0.98,
                AutocorrectionRecommendation.Candidate);
        }

        if ((options.UseExternalProvider || _settingsService?.Current.ExternalSpellingEngineEnabled == true)
            && _externalEvaluator is not null
            && _externalProvider is not null)
        {
            var externalResult = _externalEvaluator.Evaluate(
                token,
                activeLanguage,
                dictionary,
                _externalProvider,
                options);
            if (externalResult.Recommendation is AutocorrectionRecommendation.Candidate
                or AutocorrectionRecommendation.Wait)
            {
                return externalResult;
            }
        }

        if (TrustedWordAnalyzer.IsTrustedOriginal(token, dictionary)
            && !RussianOrthographyHeuristics.IsLikelyTerminalSoftSignOmission(
                token,
                activeLanguage))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        var analysis = MutationClassificationOracle.Analyze(token, activeLanguage, dictionary, options);
        if (analysis.Class == MutationOracleClass.MutationIsExactKnownWord)
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        if (analysis.Class is MutationOracleClass.Ambiguous
            or MutationOracleClass.InvalidMutation
            or MutationOracleClass.ProtectedMutation
            or MutationOracleClass.CrossLanguageCollision)
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.Wait);
        }

        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || string.IsNullOrEmpty(analysis.UniqueTarget))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.NoChange);
        }

        var uniqueTarget = analysis.UniqueTarget;
        var generatedCandidates = AutocorrectionCandidateGenerator.Generate(
            token.ToLowerInvariant(),
            activeLanguage,
            options);
        var matched = generatedCandidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Word, uniqueTarget, StringComparison.OrdinalIgnoreCase));

        var score = matched.Word is null
            ? 0.82
            : AutocorrectionCandidateRanker.ScoreCandidate(
                token.ToLowerInvariant(),
                matched,
                activeLanguage,
                dictionary,
                options,
                originalIsTrustedWord: false);

        if (score < options.CandidateThreshold)
        {
            score = options.CandidateThreshold;
        }

        if (!OperationPrecisionGate.AllowsSpellingApply(
                token,
                uniqueTarget,
                activeLanguage,
                dictionary,
                options))
        {
            return BuildResult(token, null, activeLanguage, 0.0, AutocorrectionRecommendation.Wait);
        }

        var recommendation = ResolveRecommendation(token.Length, score, options);
        var candidateToken = CapitalizationPreserver.Apply(token, uniqueTarget);
        return BuildResult(token, candidateToken, activeLanguage, score, recommendation);
    }

    private static bool IsLanguageSupported(string token, TypingLanguage activeLanguage)
    {
        return activeLanguage switch
        {
            TypingLanguage.English => TokenScriptAnalyzer.Classify(token) == TokenScript.Latin,
            TypingLanguage.Russian => TokenScriptAnalyzer.Classify(token) == TokenScript.Cyrillic,
            _ => false,
        };
    }

    private static CandidateSelection SelectBestCandidate(
        string originalToken,
        IReadOnlyList<GeneratedAutocorrectionCandidate> generatedCandidates,
        TypingLanguage activeLanguage,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        double originalPlausibility,
        bool originalIsTrustedWord,
        CandidateAmbiguityIndex ambiguityIndex)
    {
        var rankedCandidates = new List<RankedAutocorrectionCandidate>();

        foreach (var generatedCandidate in generatedCandidates)
        {
            var score = AutocorrectionCandidateRanker.ScoreCandidate(
                originalToken,
                generatedCandidate,
                activeLanguage,
                dictionary,
                options,
                originalIsTrustedWord);

            if (score <= 0.0)
            {
                continue;
            }

            if (originalIsTrustedWord
                && !AutocorrectionCandidateRanker.IsClearlyBetterThanValidOriginal(
                    score,
                    originalPlausibility,
                    options))
            {
                continue;
            }

            rankedCandidates.Add(new RankedAutocorrectionCandidate(
                generatedCandidate.Word,
                score,
                dictionary.GetFrequency(generatedCandidate.Word, activeLanguage),
                AutocorrectionCandidateRanker.GetDictionaryTier(
                    generatedCandidate.Word,
                    activeLanguage,
                    dictionary),
                generatedCandidate.EditCost));
        }

        if (rankedCandidates.Count == 0)
        {
            return CandidateSelection.Empty;
        }

        rankedCandidates.Sort(AutocorrectionCandidateRanker.CompareRankedCandidates);

        var uniqueRepeatedReduction = FindUniqueRepeatedReductionCandidate(
            originalToken,
            rankedCandidates);
        if (uniqueRepeatedReduction is RankedAutocorrectionCandidate repeatedWinner)
        {
            if (ambiguityIndex.HasAmbiguousCompetition(
                    originalToken,
                    activeLanguage,
                    repeatedWinner.Word,
                    repeatedWinner.EditCost,
                    repeatedWinner.Frequency))
            {
                return CandidateSelection.Ambiguous;
            }

            return new CandidateSelection((repeatedWinner.Word, repeatedWinner.Score), false);
        }

        var minEditCost = rankedCandidates.Min(static candidate => candidate.EditCost);
        var nearBand = rankedCandidates
            .Where(candidate => candidate.EditCost <= minEditCost + 0.30)
            .Where(candidate => candidate.Frequency > 0)
            .ToList();

        // Top-tier everyday words (the/and/their) must beat adjacent-key
        // near-misses like ten/thief when edit cost remains near-minimal.
        var topTier = nearBand
            .Where(candidate => candidate.Frequency >= 0.995)
            .OrderBy(static candidate => candidate.EditCost)
            .ThenByDescending(static candidate => candidate.Score)
            .ToList();
        if (topTier.Count >= 1 && topTier[0].EditCost <= minEditCost + 0.15)
        {
            var top = topTier[0];
            var topChallenger = nearBand
                .Where(candidate => !string.Equals(candidate.Word, top.Word, StringComparison.Ordinal))
                .OrderByDescending(static candidate => candidate.Frequency)
                .FirstOrDefault();
            if (topChallenger.Word is null
                || top.Frequency >= topChallenger.Frequency + 0.004
                || top.EditCost <= topChallenger.EditCost + 0.05)
            {
                return new CandidateSelection((top.Word, top.Score), false);
            }
        }

        // Everyday ultra-high-frequency targets win short typos when the
        // frequency margin is decisive (teh→the, adn→and, thier→their).
        if (nearBand.Count >= 1)
        {
            var frequencyOrdered = nearBand
                .OrderByDescending(static candidate => candidate.Frequency)
                .ThenBy(static candidate => candidate.EditCost)
                .ThenByDescending(static candidate => candidate.Score)
                .ToList();
            var frequencyChampion = frequencyOrdered[0];
            if (frequencyChampion.Frequency >= 0.90)
            {
                var challenger = frequencyOrdered.Skip(1).FirstOrDefault();
                if (challenger.Word is null
                    || frequencyChampion.Frequency >= challenger.Frequency + 0.12
                    || frequencyChampion.EditCost + 0.05 < challenger.EditCost)
                {
                    if (!ambiguityIndex.HasAmbiguousCompetition(
                            originalToken,
                            activeLanguage,
                            frequencyChampion.Word,
                            frequencyChampion.EditCost,
                            frequencyChampion.Frequency)
                        || challenger.Word is null
                        || frequencyChampion.Frequency >= challenger.Frequency + 0.20)
                    {
                        return new CandidateSelection((frequencyChampion.Word, frequencyChampion.Score), false);
                    }
                }
            }
        }

        var minimumCostGroup = rankedCandidates
            .Where(candidate => candidate.EditCost <= minEditCost + 0.05)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Frequency)
            .ThenBy(static candidate => candidate.Word, StringComparer.Ordinal)
            .ToList();

        if (minimumCostGroup.Count >= 2)
        {
            var bestMin = minimumCostGroup[0];
            var secondMin = minimumCostGroup[1];
            if (!HasClearFrequencyWinner(bestMin, secondMin)
                || bestMin.Score - secondMin.Score <= options.AmbiguousCandidateScoreGap)
            {
                return CandidateSelection.Ambiguous;
            }
        }

        RankedAutocorrectionCandidate preferred = minimumCostGroup[0];

        var nearEqualCompetitors = rankedCandidates
            .Where(candidate => candidate.EditCost <= preferred.EditCost + 0.30)
            .Where(candidate => candidate.Frequency > 0)
            .Where(candidate => !string.Equals(candidate.Word, preferred.Word, StringComparison.Ordinal))
            .ToList();

        if (nearEqualCompetitors.Count > 0)
        {
            var strongestCompetitor = nearEqualCompetitors
                .OrderBy(static candidate => candidate.EditCost)
                .ThenByDescending(static candidate => candidate.Score)
                .First();

            var clearWinner = preferred.EditCost + 0.05 < strongestCompetitor.EditCost
                || (HasClearFrequencyWinner(preferred, strongestCompetitor)
                    && preferred.Score - strongestCompetitor.Score > options.AmbiguousCandidateScoreGap);

            if (!clearWinner)
            {
                return CandidateSelection.Ambiguous;
            }
        }

        if (ambiguityIndex.HasAmbiguousCompetition(
                originalToken,
                activeLanguage,
                preferred.Word,
                preferred.EditCost,
                preferred.Frequency))
        {
            return CandidateSelection.Ambiguous;
        }

        return new CandidateSelection((preferred.Word, preferred.Score), false);
    }

    private static RankedAutocorrectionCandidate? FindUniqueRepeatedReductionCandidate(
        string originalToken,
        IReadOnlyList<RankedAutocorrectionCandidate> rankedCandidates)
    {
        RankedAutocorrectionCandidate? unique = null;
        foreach (var candidate in rankedCandidates)
        {
            if (!AutocorrectionCandidateRanker.IsRepeatedCharacterReduction(originalToken, candidate.Word))
            {
                continue;
            }

            if (unique is not null)
            {
                return null;
            }

            unique = candidate;
        }

        return unique;
    }

    private static bool HasClearFrequencyWinner(
        RankedAutocorrectionCandidate best,
        RankedAutocorrectionCandidate second)
    {
        return best.Frequency >= 0.85
            && second.Frequency <= best.Frequency - 0.12;
    }

    private static AutocorrectionRecommendation ResolveRecommendation(
        int tokenLength,
        double confidenceScore,
        AutocorrectionOptions options)
    {
        if (tokenLength <= 2)
        {
            return AutocorrectionRecommendation.Wait;
        }

        if (confidenceScore >= options.CandidateThreshold)
        {
            return AutocorrectionRecommendation.Candidate;
        }

        if (confidenceScore >= options.WaitThreshold)
        {
            return AutocorrectionRecommendation.Wait;
        }

        return AutocorrectionRecommendation.NoChange;
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

    private static AutocorrectionResult BuildResult(
        string originalToken,
        string? candidateToken,
        TypingLanguage activeLanguage,
        double confidenceScore,
        AutocorrectionRecommendation recommendation)
    {
        return new AutocorrectionResult
        {
            OriginalToken = originalToken,
            CandidateToken = candidateToken,
            ConfidenceScore = confidenceScore,
            Recommendation = recommendation,
            ActiveLanguage = activeLanguage,
        };
    }

    private readonly struct CandidateSelection
    {
        public static CandidateSelection Empty => new(null, false);

        public static CandidateSelection Ambiguous => new(null, true);

        public CandidateSelection((string Word, double Score)? candidate, bool isAmbiguous)
        {
            Candidate = candidate;
            IsAmbiguous = isAmbiguous;
        }

        public (string Word, double Score)? Candidate { get; }

        public bool IsAmbiguous { get; }
    }
}
