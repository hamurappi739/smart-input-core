using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

internal enum AuditTerminalCategory
{
    Preserved,
    CorrectlyCorrected,
    DominanceAccepted,
    AmbiguousWait,
    NoCandidate,
    BlockedProtected,
    BlockedPolicy,
    WrongConfident,
    SkippedInvalidCorpusEntry,
    FailedEvaluation,
}

internal sealed class MaximumCorpusAuditReport
{
    public int Seed { get; init; }
    public string CorpusFingerprint { get; init; } = string.Empty;
    public int TotalTokensEvaluated { get; set; }

    public int RussianExactWords { get; set; }
    public int EnglishExactWords { get; set; }
    public int EnglishExactServiceWords { get; set; }
    public int ExactWordsPreserved { get; set; }
    public int ExactWordsChanged { get; set; }
    public int ExactServiceWordsCorrectlyConverted { get; set; }
    public int ExactServiceWordsWaited { get; set; }
    public int ExactServiceWordsWrong { get; set; }

    public int KnownToKnownCollisionsFound { get; set; }
    public int KnownToKnownFalseSubstitutions { get; set; }

    public int RussianMutationsEvaluated { get; set; }
    public int EnglishMutationsEvaluated { get; set; }
    public int LayoutPairsEvaluated { get; set; }
    public int CombinedCasesEvaluated { get; set; }

    public int EligibleUnambiguousMutations { get; set; }
    public int CorrectMutationRecoveries { get; set; }
    public int MissedRecoveries { get; set; }
    public int AmbiguousMutationsIgnored { get; set; }
    public int WrongConfidentCorrections { get; set; }
    public int AmbiguousSpellingApplied { get; set; }
    public int AmbiguousDirectLayoutApplied { get; set; }
    public int AmbiguousCombinedApplied { get; set; }
    public int AmbiguousUnknownNameApplied { get; set; }
    public int AmbiguousPreparedLayoutApplied { get; set; }
    public int AmbiguousWhitelistApplied { get; set; }
    public int TotalAmbiguousApplied { get; set; }
    public int AmbiguousSafelyWaited { get; set; }
    public int AmbiguousDominanceAccepted { get; set; }
    public int AmbiguousLayoutAnchorAccepted { get; set; }
    public int AmbiguousNoCandidate { get; set; }
    public int AmbiguousBlocked { get; set; }
    public int AmbiguousFailed { get; set; }
    public int WrongUniqueTarget { get; set; }
    public int WrongDirectLayout { get; set; }
    public int WrongDirectLayoutMutation { get; set; }
    public int WrongDirectLayoutPairHarness { get; set; }
    public int AmbiguousDirectLayoutApply { get; set; }
    public int MustPreserveApplied { get; set; }
    public int MustWaitApplied { get; set; }
    public int WrongCombined { get; set; }
    public int WrongUnknownName { get; set; }
    public int WrongPreparedLayout { get; set; }
    public int SafelyWaited { get; set; }
    public int MutationAmbiguousSafelyWaited { get; set; }
    public int UnaccountedTokens { get; set; }
    public int DoubleCountedTokens { get; set; }

    public Dictionary<UniqueMutationTerminal, int> UniqueTerminalCounts { get; } = new();
    public Dictionary<AmbiguousMutationTerminal, int> AmbiguousTerminalCounts { get; } = new();
    public Dictionary<AmbiguousAppliedReason, int> AmbiguousAppliedReasonCounts { get; } = new();
    public Dictionary<OracleProductionDisagreementReason, int> DisagreementReasonCounts { get; } = new();

    public int ApplyGateCallCount { get; set; }
    public int TokenLevelMutationDecisions { get; set; }
    public int OracleProductionDisagreementCount { get; set; }
    public int OracleTargetsMissingFromProduction { get; set; }
    public int ProductionTargetsMissingFromOracle { get; set; }
    public int TargetsPresentWithDifferentOperation { get; set; }
    public int TargetsPresentWithDifferentCost { get; set; }
    public int TargetsPresentWithDifferentTier { get; set; }
    public int TargetsPresentWithDifferentFrequencyBand { get; set; }
    public int LayoutAlternativeMissing { get; set; }
    public int CombinedAlternativeMissing { get; set; }
    public int BruteForceOracleAgreements { get; set; }
    public int BruteForceOracleDisagreements { get; set; }
    public int BruteForceCasesEvaluated { get; set; }
    public int BruteForceProductionAgreements { get; set; }
    public int BruteForceProductionDisagreements { get; set; }
    public int ProductionSignatureIndexMiss { get; set; }
    public int IndexLookupCount { get; set; }
    public int IndexCompetitorsReturnedTotal { get; set; }
    public int IndexMaxCompetitorsReturned { get; set; }
    public double IndexAverageCompetitorsReturned =>
        IndexLookupCount == 0 ? 0 : (double)IndexCompetitorsReturnedTotal / IndexLookupCount;
    public double IndexLookupP95Ms { get; set; }
    public double IndexLookupP99Ms { get; set; }
    public double IndexLookupMaxMs { get; set; }
    public long IndexMemoryEstimateBytes { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public List<double> IndexLookupDurationsMs { get; } = [];
    public int MandatoryRegressionTotal { get; set; }
    public int MandatoryRegressionPassed { get; set; }
    public int MandatoryRegressionFailed { get; set; }
    public int MandatoryRegressionSkipped { get; set; }
    public int ApplyGateAllowedCount { get; set; }
    public int ApplyGateDeniedCount { get; set; }
    public int GateNotCalledCount { get; set; }
    public int GateCalledButAllowedCount { get; set; }
    public int OracleProductionOperationMatch { get; set; }
    public int OracleProductionOperationMismatch { get; set; }

    public int AmbiguousAppliedBeforeAccountingFix { get; set; }
    public int WrongUniqueTargetBeforeAccountingFix { get; set; }
    public double RecoveryRate =>
        EligibleUnambiguousMutations == 0
            ? 1.0
            : (double)CorrectMutationRecoveries / EligibleUnambiguousMutations;

    public int TotalMutations { get; set; }
    public int OracleUniquelyRecoverable { get; set; }
    public int OracleAmbiguous { get; set; }
    public int OracleExactKnown { get; set; }
    public int OracleInvalidOrProtected { get; set; }

    public Dictionary<EditOperationType, int> WrongConfidentByOperation { get; } = new();
    public Dictionary<EditOperationType, int> UniquelyRecoverableByOperation { get; } = new();
    public Dictionary<EditOperationType, int> CorrectRecoveriesByOperation { get; } = new();
    public Dictionary<EditOperationType, int> MissedRecoveriesByOperation { get; } = new();

    public int LayoutShouldConvert { get; set; }
    public int LayoutShouldConvertCorrect { get; set; }
    public int LayoutMustPreserve { get; set; }
    public int LayoutMustPreserveCorrect { get; set; }
    public int LayoutMustWait { get; set; }
    public int LayoutMustWaitCorrect { get; set; }
    public int WrongLayoutTargets { get; set; }
    public int WrongCombinedTargets { get; set; }

    public int DirectLayoutAccepted { get; set; }
    public int DirectLayoutRejected { get; set; }
    public int CombinedAccepted { get; set; }
    public int CombinedRejected { get; set; }

    public int MaxGeneratedCandidates { get; set; }
    public int SkippedCorpusLines { get; set; }
    public int SkippedInvalidCorpusEntries { get; set; }

    public Dictionary<AuditTerminalCategory, int> TerminalCounts { get; } = new();
    public Dictionary<MutationAmbiguityCluster, int> WrongConfidentClusters { get; } = new();
    public Dictionary<(EditOperationType Operation, OracleProductionDisagreementReason Reason), int> OperationDisagreementCounts { get; } = new();
    public Dictionary<ProductionSignatureIndexKind, int> IndexMissesBySignature { get; } = new();
    public List<string> BruteForceFailureSamples { get; } = [];
    public List<string> IndexMissSamples { get; } = [];
    public Dictionary<BruteForceOracleDisagreementReason, int> BruteForceOracleDisagreementReasons { get; } = new();
    public List<AuditCaseSample> AuditSamples { get; } = [];
    public List<string> FailureSamples { get; } = [];
    public List<string> DiagnosticClusterSamples { get; } = [];
    public List<string> LayoutPairDiagnostics { get; } = [];

    /// <summary>
    /// Developer-only synthetic corpus examples (raw tokens). Never copy into production logs/DTOs.
    /// </summary>
    public List<string> DeveloperSyntheticAmbiguousAppliedExamples { get; } = [];

    /// <summary>
    /// Developer-only: Unique intended recoveries forced to Wait by discarded-parent / R1.
    /// </summary>
    public List<string> DeveloperSyntheticR1ForcedWaitExamples { get; } = [];

    /// <summary>
    /// Developer-only: Unique==intended Wait where R1 HasDiscarded was false (other apply-gate / joint Wait).
    /// </summary>
    public List<string> DeveloperSyntheticNonR1ForcedWaitExamples { get; } = [];

    /// <summary>
    /// Developer-only synthetic layout-pair harness failures (raw tokens).
    /// </summary>
    public List<string> DeveloperSyntheticLayoutPairFailures { get; } = [];

    /// <summary>
    /// Privacy-safe identifiers for layout-pair failures. These are useful in
    /// test output without exposing synthetic token text.
    /// </summary>
    public List<string> PrivacySafeLayoutPairFailureCaseIds { get; } = [];

    public double AverageMs { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public double MaxMs { get; set; }
    public long ElapsedMs { get; set; }

    public void AddTerminal(AuditTerminalCategory category)
    {
        TerminalCounts.TryGetValue(category, out var count);
        TerminalCounts[category] = count + 1;
    }

    public int SumTerminalCategories() => TerminalCounts.Values.Sum();

    public void ReconcileWrongConfidentTotal()
    {
        CorpusAuditAccounting.SyncCanonicalCounters(this);
        WrongConfidentCorrections =
            TotalAmbiguousApplied
            + WrongUniqueTarget
            + WrongDirectLayout
            + WrongCombined
            + WrongUnknownName
            + WrongPreparedLayout;
    }

    public void SyncLegacyMissedFromTerminals()
    {
        MissedRecoveries = CorpusAuditAccounting.ComputeMissedRecoveries(this);
    }

    public string FormatAccountingSummary() => CorpusAuditAccounting.FormatAccountingSummary(this);

    public string FormatSummary()
    {
        var terminals = string.Join(
            ",",
            TerminalCounts.OrderBy(static pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
        return
            $"seed={Seed}; fingerprint={CorpusFingerprint}; tokens={TotalTokensEvaluated}; "
            + $"ruExact={RussianExactWords}; enExact={EnglishExactWords}; enServiceExact={EnglishExactServiceWords}; "
            + $"exactPreserved={ExactWordsPreserved}; exactChanged={ExactWordsChanged}; "
            + $"serviceConverted={ExactServiceWordsCorrectlyConverted}; "
            + $"collisions={KnownToKnownCollisionsFound}; collisionFalse={KnownToKnownFalseSubstitutions}; "
            + $"ruMut={RussianMutationsEvaluated}; enMut={EnglishMutationsEvaluated}; "
            + $"eligible={EligibleUnambiguousMutations}; recoveries={CorrectMutationRecoveries}; "
            + $"missed={MissedRecoveries}; ambiguous={AmbiguousMutationsIgnored}; "
            + $"wrongConfident={WrongConfidentCorrections}; recoveryRate={RecoveryRate:P2}; "
            + $"totalAmbiguousApplied={TotalAmbiguousApplied}; wrongUnique={WrongUniqueTarget}; "
            + $"tokenDecisions={TokenLevelMutationDecisions}; gateCalls={ApplyGateCallCount}; "
            + $"wrongLayout={WrongDirectLayout} (mutation={WrongDirectLayoutMutation}/pair={WrongDirectLayoutPairHarness}/"
            + $"ambLayout={AmbiguousDirectLayoutApply}/mustPreserve={MustPreserveApplied}/mustWait={MustWaitApplied}); "
            + $"wrongCombined={WrongCombined}; "
             + $"safelyWaited={SafelyWaited}; "
             + $"dominanceAccepted={AmbiguousDominanceAccepted}; "
             + $"layoutAnchorAccepted={AmbiguousLayoutAnchorAccepted}; "
            + $"totalMut={TotalMutations}; uniqueOracle={OracleUniquelyRecoverable}; "
            + $"ambiguousOracle={OracleAmbiguous}; exactKnownOracle={OracleExactKnown}; "
            + $"invalidOracle={OracleInvalidOrProtected}; "
            + $"layoutShould={LayoutShouldConvert}/{LayoutShouldConvertCorrect}; "
            + $"layoutPreserve={LayoutMustPreserve}/{LayoutMustPreserveCorrect}; "
            + $"layoutWait={LayoutMustWait}/{LayoutMustWaitCorrect}; "
            + $"wrongLayout={WrongLayoutTargets}; wrongCombined={WrongCombinedTargets}; "
             + $"layoutPairs={LayoutPairsEvaluated}; combined={CombinedCasesEvaluated}; "
             + $"layoutFailureCaseIds={string.Join(',', PrivacySafeLayoutPairFailureCaseIds.Take(8))}; "
             + $"maxCandidates={MaxGeneratedCandidates}; skippedInvalid={SkippedInvalidCorpusEntries}; "
            + $"terminals=[{terminals}]; terminalSum={SumTerminalCategories()}; "
            + $"avgMs={AverageMs:F3}; p95Ms={P95Ms:F3}; p99Ms={P99Ms:F3}; maxMs={MaxMs:F3}; "
            + $"elapsedMs={ElapsedMs}; indexMemoryBytes={IndexMemoryEstimateBytes}; "
            + $"peakWorkingSetBytes={PeakWorkingSetBytes}";
    }
}

internal static class MaximumCorpusAuditHarness
{
    private static readonly char[] RussianVowels = ['а', 'е', 'ё', 'и', 'о', 'у', 'ы', 'э', 'ю', 'я'];

    private static readonly string[] MandatoryLayoutAnchors =
    [
        "ghbdtn=>привет",
        "руддщ=>hello",
        "nen=>тут",
        "vtyzq=>меняй",
        "vbyzq=>меняй",
        "gtie=>пишу",
        "gbie=>пишу",
        "мущ=>veo",
        "пзг=>gpu",
    ];

    internal static MaximumCorpusAuditReport RunFullAudit(
        int seed = 42,
        int minRussianMutations = 125_000,
        int minEnglishMutations = 125_000)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        // Reuse the process-wide ambiguity index (no second ~600MB build).
        var ambiguityIndex = CandidateAmbiguityIndex.ForStarterLexicon();
        var autocorrection = new AutocorrectionService(ambiguityIndex);
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            autocorrection,
            converter);

        var report = new MaximumCorpusAuditReport
        {
            Seed = seed,
            CorpusFingerprint = ComputeCorpusFingerprint(),
        };
        report.IndexMemoryEstimateBytes = ambiguityIndex.EstimatedBytes;
        report.PeakWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64;

        var mandatory = MandatoryRegressionRunner.EvaluateAll(joint, dictionary);
        report.MandatoryRegressionTotal = mandatory.Total;
        report.MandatoryRegressionPassed = mandatory.Passed;
        report.MandatoryRegressionFailed = mandatory.Failed;
        report.MandatoryRegressionSkipped = mandatory.Skipped;

        var auditSampler = new StratifiedAuditSampler(seed);

        ApplyGateTelemetry.Reset();

        var durations = new List<double>(8_192);
        var stopwatch = Stopwatch.StartNew();
        var random = new Random(seed);

        var russianWords = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian)
            .Select(pair => pair.Key.Word)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToList();

        var englishWords = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.English)
            .Select(pair => pair.Key.Word)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToList();

        report.RussianExactWords = russianWords.Count;
        report.EnglishExactWords = englishWords.Count;

        foreach (var word in russianWords)
        {
            EvaluateExact(word, joint, dictionary, report, durations, expectLayoutServiceConversion: false);
        }

        foreach (var word in englishWords)
        {
            var isService = LayoutServiceWordWhitelist.TryGetRussianReplacement(word, out var russian);
            if (isService)
            {
                report.EnglishExactServiceWords++;
                EvaluateExactServiceWord(word, russian, joint, dictionary, report, durations);
            }
            else
            {
                EvaluateExact(word, joint, dictionary, report, durations, expectLayoutServiceConversion: false);
            }
        }

        AuditKnownToKnownCollisions(russianWords, joint, dictionary, report, random, durations);
        AuditLabelledLayoutTruthSet(
            russianWords,
            englishWords,
            converter,
            joint,
            dictionary,
            report,
            durations,
            ambiguityIndex);
        EvaluateMandatoryLayoutAnchors(joint, dictionary, report, durations);
        report.PeakWorkingSetBytes = Math.Max(
            report.PeakWorkingSetBytes,
            Process.GetCurrentProcess().WorkingSet64);
        // Bound retained diagnostics before the large mutation phases.
        if (report.LayoutPairDiagnostics.Count > 200)
        {
            report.LayoutPairDiagnostics.RemoveRange(200, report.LayoutPairDiagnostics.Count - 200);
        }

        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        report.PeakWorkingSetBytes = Math.Max(
            report.PeakWorkingSetBytes,
            Process.GetCurrentProcess().WorkingSet64);

        GenerateMutations(
            russianWords,
            TypingLanguage.Russian,
            minRussianMutations,
            joint,
            dictionary,
            converter,
            ambiguityIndex,
            report,
            random,
            durations,
            isRussian: true,
            auditSampler);

        GenerateMutations(
            englishWords,
            TypingLanguage.English,
            minEnglishMutations,
            joint,
            dictionary,
            converter,
            ambiguityIndex,
            report,
            random,
            durations,
            isRussian: false,
            auditSampler);

        stopwatch.Stop();
        report.ElapsedMs = stopwatch.ElapsedMilliseconds;
        report.PeakWorkingSetBytes = Math.Max(
            report.PeakWorkingSetBytes,
            Process.GetCurrentProcess().WorkingSet64);
        FinalizeDurations(report, durations);
        WriteDiagnosticArtifact(report);
        report.ApplyGateCallCount = ApplyGateTelemetry.GateCallCount;
        report.ApplyGateAllowedCount = ApplyGateTelemetry.GateAllowedCount;
        report.ApplyGateDeniedCount = ApplyGateTelemetry.GateDeniedCount;
        RunBruteForceDiagnostics(report, dictionary, ambiguityIndex, auditSampler);
        FinalizeIndexLookupStats(report);
        BruteForceOracleDisagreementClassifier.AssertReconciles(report);
        report.WrongUniqueTargetBeforeAccountingFix = report.WrongUniqueTarget;
        report.SyncLegacyMissedFromTerminals();
        report.ReconcileWrongConfidentTotal();
        OperationDisagreementCrossTable.AssertReconciles(report);
        CorpusAuditAccounting.AssertAllReconciliations(report);
        report.PeakWorkingSetBytes = Math.Max(
            report.PeakWorkingSetBytes,
            Process.GetCurrentProcess().WorkingSet64);
        // Drop retained duration samples after percentiles are computed.
        report.IndexLookupDurationsMs.Clear();
        durations.Clear();
        return report;
    }

    private static void FinalizeIndexLookupStats(MaximumCorpusAuditReport report)
    {
        if (report.IndexLookupDurationsMs.Count == 0)
        {
            return;
        }

        var sorted = report.IndexLookupDurationsMs.OrderBy(static value => value).ToList();
        report.IndexLookupP95Ms = sorted[(int)(sorted.Count * 0.95)];
        report.IndexLookupP99Ms = sorted[(int)(sorted.Count * 0.99)];
        report.IndexLookupMaxMs = sorted[^1];
    }

    private static void RunBruteForceDiagnostics(
        MaximumCorpusAuditReport report,
        IAutocorrectDictionary dictionary,
        CandidateAmbiguityIndex ambiguityIndex,
        StratifiedAuditSampler auditSampler)
    {
        var options = new AutocorrectionOptions();

        foreach (var assertion in MandatoryRegressionCatalog.All.Where(a => a.Kind is MandatoryRegressionKind.MustApply
                     or MandatoryRegressionKind.MustApplyWithContext
                     or MandatoryRegressionKind.MustApplyCapitalized))
        {
            auditSampler.Consider(new AuditCaseSample
            {
                Token = assertion.Input,
                Language = TokenScriptAnalyzer.Classify(assertion.Input) == TokenScript.Cyrillic
                    ? TypingLanguage.Russian
                    : TypingLanguage.English,
                IntendedSource = assertion.ExpectedOutput,
                Kind = AuditSampleKind.MandatoryRegression,
            });
        }

        BruteForceAuditRunner.RunExpandedVerification(
            report,
            auditSampler.Samples.Concat(report.AuditSamples).DistinctBy(s => $"{s.Kind}:{s.Token}:{s.Language}").ToList(),
            dictionary,
            ambiguityIndex,
            options);

        // Index completeness already audited inline over all mutations during GenerateMutations.
    }

    private static void EvaluateExact(
        string word,
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        MaximumCorpusAuditReport report,
        List<double> durations,
        bool expectLayoutServiceConversion)
    {
        _ = expectLayoutServiceConversion;
        var local = Stopwatch.StartNew();
        JointCorrectionDecisionResult result;
        try
        {
            result = joint.Evaluate(word, dictionary, true, true);
        }
        catch (Exception exception)
        {
            local.Stop();
            durations.Add(local.Elapsed.TotalMilliseconds);
            report.TotalTokensEvaluated++;
            report.AddTerminal(AuditTerminalCategory.FailedEvaluation);
            AddFailure(report, $"exactFail:{word}:{exception.GetType().Name}");
            return;
        }

        local.Stop();
        durations.Add(local.Elapsed.TotalMilliseconds);
        report.TotalTokensEvaluated++;

        if (ProtectedTokenAnalyzer.IsProtected(word))
        {
            report.AddTerminal(AuditTerminalCategory.BlockedProtected);
            report.ExactWordsPreserved++;
            return;
        }

        if (result.Recommendation == JointCorrectionRecommendation.Apply)
        {
            report.ExactWordsChanged++;
            report.AddTerminal(AuditTerminalCategory.WrongConfident);
            AddFailure(report, $"{word}->{result.ReplacementToken}/{result.Kind}");
        }
        else if (result.Recommendation == JointCorrectionRecommendation.Wait)
        {
            report.ExactWordsPreserved++;
            report.AddTerminal(AuditTerminalCategory.AmbiguousWait);
        }
        else
        {
            report.ExactWordsPreserved++;
            report.AddTerminal(AuditTerminalCategory.Preserved);
        }
    }

    private static void EvaluateExactServiceWord(
        string word,
        string expectedRussian,
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        MaximumCorpusAuditReport report,
        List<double> durations)
    {
        var local = Stopwatch.StartNew();
        var result = joint.Evaluate(word, dictionary, true, true);
        local.Stop();
        durations.Add(local.Elapsed.TotalMilliseconds);
        report.TotalTokensEvaluated++;

        if (result.Recommendation == JointCorrectionRecommendation.Apply
            && string.Equals(result.ReplacementToken, expectedRussian, StringComparison.OrdinalIgnoreCase))
        {
            report.ExactServiceWordsCorrectlyConverted++;
            report.AddTerminal(AuditTerminalCategory.CorrectlyCorrected);
            return;
        }

        if (result.Recommendation is JointCorrectionRecommendation.Wait or JointCorrectionRecommendation.NoChange)
        {
            report.ExactServiceWordsWaited++;
            report.AddTerminal(AuditTerminalCategory.AmbiguousWait);
            return;
        }

        report.ExactServiceWordsWrong++;
        report.ExactWordsChanged++;
        report.AddTerminal(AuditTerminalCategory.WrongConfident);
        AddFailure(report, $"service:{word}->{result.ReplacementToken} expected {expectedRussian}");
    }

    private static void AuditKnownToKnownCollisions(
        IReadOnlyList<string> russianWords,
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        MaximumCorpusAuditReport report,
        Random random,
        List<double> durations)
    {
        var set = russianWords.Where(word => word.Length is >= 2 and <= 10).ToHashSet(StringComparer.Ordinal);
        var sample = russianWords
            .Where(word => word.Length is >= 2 and <= 8)
            .Where((_, index) => index % 5 == 0)
            .Take(12_000)
            .ToList();

        foreach (var word in sample)
        {
            foreach (var neighbor in GenerateCollisionNeighbors(word, random))
            {
                if (!set.Contains(neighbor) || string.Equals(word, neighbor, StringComparison.Ordinal))
                {
                    continue;
                }

                report.KnownToKnownCollisionsFound++;
                var local = Stopwatch.StartNew();
                var result = joint.Evaluate(word, dictionary, true, true);
                local.Stop();
                durations.Add(local.Elapsed.TotalMilliseconds);
                report.TotalTokensEvaluated++;

                if (result.Recommendation == JointCorrectionRecommendation.Apply
                    && string.Equals(result.ReplacementToken, neighbor, StringComparison.OrdinalIgnoreCase))
                {
                    report.KnownToKnownFalseSubstitutions++;
                    report.AddTerminal(AuditTerminalCategory.WrongConfident);
                    AddFailure(report, $"collision:{word}->{neighbor}");
                }
                else if (result.Recommendation == JointCorrectionRecommendation.Wait)
                {
                    report.AddTerminal(AuditTerminalCategory.AmbiguousWait);
                }
                else
                {
                    report.AddTerminal(AuditTerminalCategory.Preserved);
                }
            }
        }
    }

    private static void AuditLabelledLayoutTruthSet(
        IReadOnlyList<string> russianWords,
        IReadOnlyList<string> englishWords,
        ILayoutConversionService converter,
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        MaximumCorpusAuditReport report,
        List<double> durations,
        CandidateAmbiguityIndex ambiguityIndex)
    {
        _ = ambiguityIndex;

        // Positive layout coverage: type the physical-layout encoding of a known
        // target word and require conversion back to that target when unambiguous.
        foreach (var target in russianWords.Where(word => word.Length is >= 3 and <= 12))
        {
            var typed = converter.Convert(target, LayoutConversionDirection.RussianToEnglish);
            EvaluateLayoutTruthCase(
                typed,
                target,
                TypingLanguage.English,
                TypingLanguage.Russian,
                joint,
                dictionary,
                report,
                durations);
        }

        foreach (var target in englishWords.Where(word => word.Length is >= 3 and <= 12))
        {
            if (LayoutServiceWordWhitelist.TryGetRussianReplacement(target, out _))
            {
                continue;
            }

            var typed = converter.Convert(target, LayoutConversionDirection.EnglishToRussian);
            EvaluateLayoutTruthCase(
                typed,
                target,
                TypingLanguage.Russian,
                TypingLanguage.English,
                joint,
                dictionary,
                report,
                durations);
        }
    }

    private static void EvaluateLayoutTruthCase(
        string typed,
        string expectedTarget,
        TypingLanguage typedLanguage,
        TypingLanguage targetLanguage,
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        MaximumCorpusAuditReport report,
        List<double> durations)
    {
        if (string.Equals(typed, expectedTarget, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(typed)
            || !typed.All(char.IsLetter))
        {
            report.SkippedInvalidCorpusEntries++;
            report.AddTerminal(AuditTerminalCategory.SkippedInvalidCorpusEntry);
            report.TotalTokensEvaluated++;
            return;
        }

        report.LayoutPairsEvaluated++;

        var typedIsStrong = dictionary.Contains(typed, typedLanguage)
            && dictionary.GetFrequency(typed, typedLanguage) >= 0.70;
        var targetIsStrong = dictionary.Contains(expectedTarget, targetLanguage)
            && dictionary.GetFrequency(expectedTarget, targetLanguage) >= 0.70;
        var typedIsAlsoKnownTargetScript = dictionary.Contains(typed, targetLanguage);
        var isRequiredLayoutAnchor = LayoutCorrectionAnchors.IsPositiveAnchor(typed);
        var isExplicitShortLayoutPair = LayoutServiceWordWhitelist.TryGetReplacement(
                typed,
                out var explicitReplacement,
                out _)
            && string.Equals(explicitReplacement, expectedTarget, StringComparison.OrdinalIgnoreCase);

        string label;
        if (typedIsStrong)
        {
            label = "MustPreserve";
            report.LayoutMustPreserve++;
        }
        else if ((targetIsStrong || isRequiredLayoutAnchor || isExplicitShortLayoutPair)
                 && !typedIsAlsoKnownTargetScript)
        {
            label = "ShouldConvert";
            report.LayoutShouldConvert++;
        }
        else
        {
            label = "MustWait";
            report.LayoutMustWait++;
        }

        var local = Stopwatch.StartNew();
        var result = joint.Evaluate(typed, dictionary, true, true);
        local.Stop();
        durations.Add(local.Elapsed.TotalMilliseconds);
        report.TotalTokensEvaluated++;

        switch (label)
        {
            case "MustPreserve":
            if (result.Recommendation == JointCorrectionRecommendation.Apply
                && result.Kind is CorrectionKind.Layout or CorrectionKind.Combined)
            {
                report.WrongDirectLayout++;
                report.WrongDirectLayoutPairHarness++;
                report.MustPreserveApplied++;
                report.WrongLayoutTargets++;
                report.DirectLayoutAccepted++;
                report.AddTerminal(AuditTerminalCategory.WrongConfident);
                AddFailure(report, $"layoutLeak:{typed}->{result.ReplacementToken}");
                RecordLayoutPairDiagnostic(
                    report,
                    typed,
                    expectedTarget,
                    typedLanguage,
                    targetLanguage,
                    "MustPreserve",
                    "NoChange",
                    result,
                    dictionary);
                RecordDeveloperLayoutPairFailure(
                    report,
                    typed,
                    expectedTarget,
                    typedLanguage,
                    targetLanguage,
                    "MustPreserve",
                    "NoChange",
                    result,
                    dictionary,
                    harnessKind: "LayoutPairHarness");
            }
                else
                {
                    report.LayoutMustPreserveCorrect++;
                    report.DirectLayoutRejected++;
                    report.AddTerminal(
                        result.Recommendation == JointCorrectionRecommendation.Wait
                            ? AuditTerminalCategory.AmbiguousWait
                            : AuditTerminalCategory.Preserved);
                }

                break;

            case "ShouldConvert":
                if (result.Recommendation == JointCorrectionRecommendation.Apply
                    && string.Equals(result.ReplacementToken, expectedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    report.LayoutShouldConvertCorrect++;
                    report.AddTerminal(AuditTerminalCategory.CorrectlyCorrected);
                    if (result.Kind == CorrectionKind.Combined)
                    {
                        report.CombinedAccepted++;
                    }
                }
                else if (result.Recommendation == JointCorrectionRecommendation.Apply)
                {
                    if (result.Kind == CorrectionKind.Combined)
                    {
                        report.WrongCombined++;
                        report.WrongCombinedTargets++;
                    }
                    else
                    {
                        report.WrongDirectLayout++;
                        report.WrongDirectLayoutPairHarness++;
                        report.WrongLayoutTargets++;
                    }

                    report.AddTerminal(AuditTerminalCategory.WrongConfident);
                    AddFailure(report, $"layoutWrong:{typed}->{result.ReplacementToken} expected {expectedTarget}");
                    RecordLayoutPairDiagnostic(
                        report,
                        typed,
                        expectedTarget,
                        typedLanguage,
                        targetLanguage,
                        "ShouldConvert",
                        "Apply",
                        result,
                        dictionary);
                    RecordDeveloperLayoutPairFailure(
                        report,
                        typed,
                        expectedTarget,
                        typedLanguage,
                        targetLanguage,
                        "ShouldConvert",
                        "Apply",
                        result,
                        dictionary,
                        harnessKind: "LayoutPairHarness");
                }
                else
                {
                    report.AddTerminal(AuditTerminalCategory.AmbiguousWait);
                    // Developer report only: ShouldConvert expected Apply but Waited/NoChange.
                    RecordDeveloperLayoutPairFailure(
                        report,
                        typed,
                        expectedTarget,
                        typedLanguage,
                        targetLanguage,
                        "ShouldConvert",
                        "Apply",
                        result,
                        dictionary,
                        harnessKind: "LayoutPairHarnessMissedConvert");
                }

                break;

            default:
                if (result.Recommendation == JointCorrectionRecommendation.Apply
                    && result.Kind is CorrectionKind.Layout or CorrectionKind.Combined)
                {
                    report.WrongDirectLayout++;
                    report.WrongDirectLayoutPairHarness++;
                    report.MustWaitApplied++;
                    report.WrongLayoutTargets++;
                    report.AddTerminal(AuditTerminalCategory.WrongConfident);
                    AddFailure(report, $"layoutMustWaitApplied:{typed}->{result.ReplacementToken}");
                    RecordLayoutPairDiagnostic(
                        report,
                        typed,
                        expectedTarget,
                        typedLanguage,
                        targetLanguage,
                        "MustWait",
                        "Wait",
                        result,
                        dictionary);
                    RecordDeveloperLayoutPairFailure(
                        report,
                        typed,
                        expectedTarget,
                        typedLanguage,
                        targetLanguage,
                        "MustWait",
                        "Wait",
                        result,
                        dictionary,
                        harnessKind: "LayoutPairHarness");
                }
                else
                {
                    report.LayoutMustWaitCorrect++;
                    if (result.Recommendation == JointCorrectionRecommendation.Wait)
                    {
                        report.SafelyWaited++;
                    }

                    report.AddTerminal(
                        result.Recommendation == JointCorrectionRecommendation.Wait
                            ? AuditTerminalCategory.AmbiguousWait
                            : result.Recommendation == JointCorrectionRecommendation.Apply
                                ? AuditTerminalCategory.CorrectlyCorrected
                                : AuditTerminalCategory.NoCandidate);
                }

                break;
        }
    }

    private static void RecordLayoutPairDiagnostic(
        MaximumCorpusAuditReport report,
        string typed,
        string expectedTarget,
        TypingLanguage typedLanguage,
        TypingLanguage targetLanguage,
        string policyVerdict,
        string expectedVerdict,
        JointCorrectionDecisionResult result,
        IAutocorrectDictionary dictionary)
    {
        if (report.LayoutPairDiagnostics.Count >= 200)
        {
            return;
        }

        var caseId = AuditCaseHasher.HashCase(typed, expectedTarget, result.ReplacementToken);
        var sourceScript = TokenScriptAnalyzer.Classify(typed).ToString();
        var targetScript = TokenScriptAnalyzer.Classify(expectedTarget).ToString();
        var category = result.Kind.ToString();
        var actual = result.Recommendation.ToString();
        var gate = "n/a";
        if (result.Recommendation == JointCorrectionRecommendation.Apply)
        {
            var verdict = BoundedCandidateApplyGuard.Evaluate(
                typed,
                result.ReplacementToken,
                result.Kind,
                typedLanguage,
                dictionary,
                new KeyboardLayoutConverter(),
                new AutocorrectionOptions());
            gate = verdict == BoundedApplyVerdict.Allow
                ? $"BoundedCandidateApplyGuard:{result.Kind}"
                : $"bypass-before-guard:{result.Kind}:{verdict}";
        }

        report.LayoutPairDiagnostics.Add(
            $"{caseId}|src={sourceScript}|tgt={targetScript}|cat={category}|policy={policyVerdict}|expected={expectedVerdict}|actual={actual}|gate={gate}");
    }

    private static void EvaluateMandatoryLayoutAnchors(
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        MaximumCorpusAuditReport report,
        List<double> durations)
    {
        var russianHint = new SentenceLanguageHint(TypingLanguage.Russian, 4, 0, 4, 20);
        foreach (var anchor in MandatoryLayoutAnchors)
        {
            var parts = anchor.Split("=>", StringSplitOptions.None);
            var source = parts[0];
            var expected = parts[1];
            var hint = string.Equals(source, "nen", StringComparison.Ordinal)
                ? russianHint
                : SentenceLanguageHint.Empty;

            var local = Stopwatch.StartNew();
            var result = joint.Evaluate(source, dictionary, true, true, languageHint: hint);
            local.Stop();
            durations.Add(local.Elapsed.TotalMilliseconds);
            report.TotalTokensEvaluated++;
            report.LayoutPairsEvaluated++;

            if (result.Recommendation == JointCorrectionRecommendation.Apply
                && string.Equals(result.ReplacementToken, expected, StringComparison.OrdinalIgnoreCase))
            {
                report.LayoutShouldConvert++;
                report.LayoutShouldConvertCorrect++;
                report.AddTerminal(AuditTerminalCategory.CorrectlyCorrected);
                if (result.Kind == CorrectionKind.Combined)
                {
                    report.CombinedAccepted++;
                }
            }
            else
            {
                report.WrongLayoutTargets++;
                report.AddTerminal(AuditTerminalCategory.WrongConfident);
                AddFailure(report, $"anchor:{source}->{result.ReplacementToken} expected {expected}");
            }
        }

        foreach (var (source, forbidden) in new (string, string)[]
                 {
                     ("миняй", "vbyzq"),
                     ("гавно", "ufdyj"),
                     ("говно", "ujdyj"),
                     ("дууш", "leei"),
                     ("душ", "lei"),
                     ("меня", "vtyz"),
                 })
        {
            var local = Stopwatch.StartNew();
            var result = joint.Evaluate(source, dictionary, true, true);
            local.Stop();
            durations.Add(local.Elapsed.TotalMilliseconds);
            report.TotalTokensEvaluated++;

            if (result.Recommendation == JointCorrectionRecommendation.Apply
                && string.Equals(result.ReplacementToken, forbidden, StringComparison.OrdinalIgnoreCase))
            {
                report.WrongLayoutTargets++;
                report.AddTerminal(AuditTerminalCategory.WrongConfident);
                AddFailure(report, $"forbiddenLayout:{source}->{forbidden}");
            }
            else
            {
                report.AddTerminal(
                    result.Recommendation == JointCorrectionRecommendation.Apply
                        ? AuditTerminalCategory.CorrectlyCorrected
                        : AuditTerminalCategory.Preserved);
            }
        }
    }

    private static void GenerateMutations(
        IReadOnlyList<string> words,
        TypingLanguage language,
        int minimumCount,
        JointCorrectionDecisionService joint,
        IAutocorrectDictionary dictionary,
        ILayoutConversionService converter,
        CandidateAmbiguityIndex ambiguityIndex,
        MaximumCorpusAuditReport report,
        Random random,
        List<double> durations,
        bool isRussian,
        StratifiedAuditSampler auditSampler)
    {
        var eligible = words
            .Select(word => (Word: word, Frequency: dictionary.GetFrequency(word, language)))
            .Where(entry => entry.Word.Length is >= 4 and <= 12)
            .Where(entry => entry.Word.All(char.IsLetter))
            .Where(entry => entry.Frequency >= 0.75)
            .Select(entry => entry.Word)
            .ToList();

        if (eligible.Count < 500)
        {
            eligible = words
                .Where(word => word.Length is >= 4 and <= 12)
                .Where(word => word.All(char.IsLetter))
                .ToList();
        }

        var evaluated = 0;
        var index = 0;
        while (evaluated < minimumCount)
        {
            var word = eligible[index % eligible.Count];
            index++;

            foreach (var mutation in CreateMutations(word, language, random))
            {
                if (evaluated >= minimumCount)
                {
                    break;
                }

                if (string.Equals(mutation, word, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mutation) || !mutation.All(char.IsLetter))
                {
                    report.SkippedInvalidCorpusEntries++;
                    report.AddTerminal(AuditTerminalCategory.SkippedInvalidCorpusEntry);
                    report.TotalTokensEvaluated++;
                    continue;
                }

                if (TrustedWordAnalyzer.IsExactKnownOriginal(mutation, dictionary))
                {
                    evaluated++;
                    report.TotalMutations++;
                    report.OracleExactKnown++;
                    if (isRussian)
                    {
                        report.RussianMutationsEvaluated++;
                    }
                    else
                    {
                        report.EnglishMutationsEvaluated++;
                    }

                    var knownResult = joint.Evaluate(mutation, dictionary, true, true);
                    report.TotalTokensEvaluated++;
                    if (knownResult.Recommendation == JointCorrectionRecommendation.Apply)
                    {
                        report.WrongConfidentCorrections++;
                        report.AddTerminal(AuditTerminalCategory.WrongConfident);
                        CountOperation(report.WrongConfidentByOperation, EditOperationType.Unknown);
                    }
                    else
                    {
                        report.AmbiguousMutationsIgnored++;
                        report.AddTerminal(
                            knownResult.Recommendation == JointCorrectionRecommendation.Wait
                                ? AuditTerminalCategory.AmbiguousWait
                                : AuditTerminalCategory.Preserved);
                    }

                    continue;
                }

                var options = new AutocorrectionOptions();
                var oracleAnalysis = IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
                    mutation,
                    word,
                    language,
                    dictionary,
                    options);
                report.TotalMutations++;

                SignatureIndexCompletenessAuditor.AuditSingleCase(
                    report,
                    mutation,
                    language,
                    word,
                    oracleAnalysis,
                    ambiguityIndex);

                switch (oracleAnalysis.Class)
                {
                    case MutationOracleClass.UniquelyRecoverable:
                        report.OracleUniquelyRecoverable++;
                        CountOperation(report.UniquelyRecoverableByOperation, oracleAnalysis.Operation);
                        break;
                    case MutationOracleClass.Ambiguous:
                        report.OracleAmbiguous++;
                        break;
                    case MutationOracleClass.MutationIsExactKnownWord:
                        report.OracleExactKnown++;
                        break;
                    default:
                        report.OracleInvalidOrProtected++;
                        break;
                }

                if (mutation.Length <= 4)
                {
                    auditSampler.Consider(new AuditCaseSample
                    {
                        Token = mutation,
                        Language = language,
                        IntendedSource = word,
                        Operation = oracleAnalysis.Operation,
                        Kind = AuditSampleKind.ShortToken,
                    });
                }

                if (language == TypingLanguage.Russian
                    && (mutation.Contains('е') || mutation.Contains('ё') || word.Contains('е') || word.Contains('ё')))
                {
                    auditSampler.Consider(new AuditCaseSample
                    {
                        Token = mutation,
                        Language = language,
                        IntendedSource = word,
                        Operation = oracleAnalysis.Operation,
                        Kind = AuditSampleKind.YeYo,
                    });
                }

                var candidates = AutocorrectionCandidateGenerator.Generate(
                    mutation,
                    language,
                    options);
                report.MaxGeneratedCandidates = Math.Max(report.MaxGeneratedCandidates, candidates.Count);

                var gateCallsBefore = ApplyGateTelemetry.GateCallCount;
                var local = Stopwatch.StartNew();
                JointCorrectionDecisionResult result;
                try
                {
                    result = joint.Evaluate(mutation, dictionary, true, true);
                }
                catch (Exception ex)
                {
                    local.Stop();
                    durations.Add(local.Elapsed.TotalMilliseconds);
                    report.TotalTokensEvaluated++;
                    evaluated++;
                    if (oracleAnalysis.Class == MutationOracleClass.UniquelyRecoverable)
                    {
                        CorpusAuditAccounting.RecordUniqueTerminal(report, UniqueMutationTerminal.ExceptionUnique);
                        report.EligibleUnambiguousMutations++;
                    }

                    report.AddTerminal(AuditTerminalCategory.FailedEvaluation);
                    AddFailure(report, $"exception:{mutation}:{ex.GetType().Name}");
                    if (isRussian)
                    {
                        report.RussianMutationsEvaluated++;
                    }
                    else
                    {
                        report.EnglishMutationsEvaluated++;
                    }

                    continue;
                }

                local.Stop();
                var gateCallsAfter = ApplyGateTelemetry.GateCallCount;
                durations.Add(local.Elapsed.TotalMilliseconds);
                report.TotalTokensEvaluated++;
                evaluated++;

                if (isRussian)
                {
                    report.RussianMutationsEvaluated++;
                }
                else
                {
                    report.EnglishMutationsEvaluated++;
                }

                if (TokenScriptAnalyzer.Classify(mutation) != TokenScriptAnalyzer.Classify(word)
                    || mutation.Length != word.Length
                    || HasLayoutLikeShape(mutation, word))
                {
                    report.CombinedCasesEvaluated++;
                }

                var expectsRecovery = oracleAnalysis.Class == MutationOracleClass.UniquelyRecoverable;
                if (expectsRecovery)
                {
                    report.EligibleUnambiguousMutations++;
                    ClassifyUniqueMutationOutcome(
                        report,
                        oracleAnalysis,
                        mutation,
                        word,
                        language,
                        dictionary,
                        options,
                        candidates,
                        result,
                        gateCallsBefore,
                        gateCallsAfter,
                        auditSampler);
                }
                else if (oracleAnalysis.Class == MutationOracleClass.Ambiguous)
                {
                    report.AmbiguousMutationsIgnored++;
                    ClassifyAmbiguousMutationOutcome(
                        report,
                        oracleAnalysis,
                        mutation,
                        word,
                        language,
                        dictionary,
                        options,
                        candidates,
                        result,
                        gateCallsBefore,
                        gateCallsAfter,
                        auditSampler);
                }
                else
                {
                    if (result.Recommendation == JointCorrectionRecommendation.Apply)
                    {
                        report.WrongConfidentCorrections++;
                        report.AddTerminal(AuditTerminalCategory.WrongConfident);
                        AddFailure(report, $"exactKnownApply:{mutation}->{result.ReplacementToken}");
                    }
                    else
                    {
                        report.SafelyWaited++;
                        report.AddTerminal(
                            result.Recommendation == JointCorrectionRecommendation.Wait
                                ? AuditTerminalCategory.AmbiguousWait
                                : AuditTerminalCategory.Preserved);
                    }
                }
            }
        }
    }

    private static void ClassifyUniqueMutationOutcome(
        MaximumCorpusAuditReport report,
        MutationAnalysisResult oracleAnalysis,
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        IReadOnlyList<GeneratedAutocorrectionCandidate> generatedCandidates,
        JointCorrectionDecisionResult result,
        int gateCallsBefore,
        int gateCallsAfter,
        StratifiedAuditSampler auditSampler)
    {
        RecordOperationAgreement(report, oracleAnalysis, mutation, language, dictionary, options, result);

        if (result.Recommendation == JointCorrectionRecommendation.Apply
            && string.Equals(result.ReplacementToken, intendedSource, StringComparison.OrdinalIgnoreCase))
        {
            report.CorrectMutationRecoveries++;
            CountOperation(report.CorrectRecoveriesByOperation, oracleAnalysis.Operation);
            CorpusAuditAccounting.RecordUniqueTerminal(report, UniqueMutationTerminal.CorrectRecovery);
            report.AddTerminal(AuditTerminalCategory.CorrectlyCorrected);
            if (result.Kind == CorrectionKind.Combined)
            {
                report.CombinedAccepted++;
            }

            return;
        }

        if (result.Recommendation == JointCorrectionRecommendation.Apply)
        {
            if (result.Kind == CorrectionKind.Combined)
            {
                report.WrongCombined++;
            }
            else if (result.Kind == CorrectionKind.Layout)
            {
                report.WrongDirectLayout++;
                report.WrongDirectLayoutMutation++;
            }

            report.WrongUniqueTarget++;
            report.WrongConfidentCorrections++;
            CountOperation(report.WrongConfidentByOperation, oracleAnalysis.Operation);
            CorpusAuditAccounting.RecordUniqueTerminal(report, UniqueMutationTerminal.WrongUniqueTarget);
            report.AddTerminal(AuditTerminalCategory.WrongConfident);
            var cluster = oracleAnalysis.Cluster == MutationAmbiguityCluster.None
                ? MutationAmbiguityCluster.CandidateScoringDefect
                : oracleAnalysis.Cluster;
            CountCluster(report, cluster);
            AddFailure(report, $"wrongUnique:{mutation}->{result.ReplacementToken} (from {intendedSource})");
            AddDiagnostic(report, cluster, mutation, intendedSource, result.ReplacementToken);
            if (report.AuditSamples.Count(sample => sample.Kind == AuditSampleKind.WrongUniqueTarget) < 100)
            {
                var sample = new AuditCaseSample
                {
                    Token = mutation,
                    Language = language,
                    IntendedSource = intendedSource,
                    Operation = oracleAnalysis.Operation,
                    Kind = AuditSampleKind.WrongUniqueTarget,
                };
                report.AuditSamples.Add(sample);
                auditSampler.Consider(sample);
            }

            return;
        }

        UniqueMutationTerminal missedTerminal = result.Recommendation switch
        {
            JointCorrectionRecommendation.Wait => UniqueMutationTerminal.WaitedUnique,
            _ => ProtectedTokenAnalyzer.IsProtected(mutation)
                ? UniqueMutationTerminal.ProtectedUnique
                : UniqueMutationTerminal.NoCandidateUnique,
        };

        if (result.Recommendation == JointCorrectionRecommendation.Wait
            && string.Equals(oracleAnalysis.UniqueTarget, intendedSource, StringComparison.OrdinalIgnoreCase))
        {
            var production = MutationClassificationOracle.Analyze(mutation, language, dictionary, options);
            if (OperationPrecisionGate.HasDiscardedCredibleCompetitor(production))
            {
                RecordDeveloperR1ForcedWaitExample(
                    report,
                    mutation,
                    intendedSource,
                    language,
                    dictionary,
                    options,
                    oracleAnalysis,
                    result);
            }
            else if (report.DeveloperSyntheticNonR1ForcedWaitExamples.Count < 500)
            {
                var auto = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon())
                    .Evaluate(mutation, language, dictionary, options);
                var caseId = AuditCaseHasher.HashCase(mutation, intendedSource, production.UniqueTarget);
                report.DeveloperSyntheticNonR1ForcedWaitExamples.Add(
                    $"caseId={caseId}|op={oracleAnalysis.Operation}|selected={production.UniqueTarget}|intended={intendedSource}|"
                    + $"spellClass={production.Class}|autoRec={auto.Recommendation}|autoCand={auto.CandidateToken}|"
                    + $"jointRec={result.Recommendation}|jointKind={result.Kind}|token={mutation}");
            }
        }

        report.MissedRecoveries++;
        CountOperation(report.MissedRecoveriesByOperation, oracleAnalysis.Operation);
        CorpusAuditAccounting.RecordUniqueTerminal(report, missedTerminal);
        report.AddTerminal(
            result.Recommendation == JointCorrectionRecommendation.Wait
                ? AuditTerminalCategory.AmbiguousWait
                : AuditTerminalCategory.NoCandidate);
    }

    private static void ClassifyAmbiguousMutationOutcome(
        MaximumCorpusAuditReport report,
        MutationAnalysisResult oracleAnalysis,
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        IReadOnlyList<GeneratedAutocorrectionCandidate> generatedCandidates,
        JointCorrectionDecisionResult result,
        int gateCallsBefore,
        int gateCallsAfter,
        StratifiedAuditSampler auditSampler)
    {
        // The independent generated-case oracle intentionally marks
        // Unique!=intended as Ambiguous so the audit can expose hidden corpus
        // remaps. A production Unique winner may nevertheless be a deliberate,
        // clearly dominant correction (the same policy that preserves the
        // mandatory ultra-frequency cases). Account for that outcome explicitly
        // instead of reporting it as a false safety violation.
        var production = MutationClassificationOracle.Analyze(
            mutation,
            language,
            dictionary,
            options);

        // The product has a very small explicit layout allow-list for
        // deterministic high-value conversions. A generated mutation can
        // land on one of these exact anchors while the independent oracle
        // labels it ambiguous by construction. Keep this intentional
        // exception visible in the audit without changing any runtime gate.
        var explicitShortLayoutAccepted = LayoutServiceWordWhitelist.TryGetReplacement(
                mutation,
                out var explicitShortReplacement,
                out _)
            && string.Equals(
                explicitShortReplacement,
                result.ReplacementToken,
                StringComparison.OrdinalIgnoreCase);
        var explicitLayoutAnchorAccepted = result.Recommendation == JointCorrectionRecommendation.Apply
            && result.Kind == CorrectionKind.Layout
            && (LayoutCorrectionAnchors.IsPositiveAnchor(mutation)
                || explicitShortLayoutAccepted)
            && !string.IsNullOrWhiteSpace(result.ReplacementToken);

        if (explicitLayoutAnchorAccepted)
        {
            CorpusAuditAccounting.RecordAmbiguousTerminal(
                report,
                AmbiguousMutationTerminal.ExplicitLayoutAnchorAccepted);
            report.AddTerminal(AuditTerminalCategory.DominanceAccepted);
            return;
        }

        var dominanceAccepted = result.Recommendation == JointCorrectionRecommendation.Apply
            && result.Kind == CorrectionKind.Autocorrect
            && production.Class == MutationOracleClass.UniquelyRecoverable
            && string.Equals(
                result.ReplacementToken,
                production.UniqueTarget,
                StringComparison.OrdinalIgnoreCase)
            && !OperationPrecisionGate.ShouldRemapUniqueAsAmbiguous(
                production,
                intendedSource);

        if (dominanceAccepted)
        {
            report.AmbiguousDominanceAccepted++;
            CorpusAuditAccounting.RecordAmbiguousTerminal(
                report,
                AmbiguousMutationTerminal.DominanceAccepted);
            report.AddTerminal(AuditTerminalCategory.DominanceAccepted);
            return;
        }

        if (result.Recommendation == JointCorrectionRecommendation.Apply)
        {
            if (result.Kind == CorrectionKind.Combined)
            {
                report.WrongCombined++;
                CorpusAuditAccounting.RecordAmbiguousTerminal(
                    report,
                    AmbiguousMutationTerminal.AmbiguousCombinedApplied);
                auditSampler.Consider(new AuditCaseSample
                {
                    Token = mutation,
                    Language = language,
                    IntendedSource = intendedSource,
                    Operation = oracleAnalysis.Operation,
                    Kind = AuditSampleKind.WrongCombined,
                });
            }
            else if (result.Kind == CorrectionKind.Layout)
            {
                report.WrongDirectLayout++;
                report.WrongDirectLayoutMutation++;
                report.AmbiguousDirectLayoutApply++;
                CorpusAuditAccounting.RecordAmbiguousTerminal(
                    report,
                    AmbiguousMutationTerminal.AmbiguousDirectLayoutApplied);
                var mutationTargetLanguage = language == TypingLanguage.Russian
                    ? TypingLanguage.English
                    : TypingLanguage.Russian;
                var mutationCaseId = AuditCaseHasher.HashCase(
                    mutation,
                    intendedSource,
                    result.ReplacementToken);
                report.DeveloperSyntheticLayoutPairFailures.Add(
                    $"caseId={mutationCaseId}|policy=MutationAmbiguous|expectedVerdict=Wait|"
                    + $"actualVerdict={result.Recommendation}/{result.Kind}|typedLang={language}|"
                    + $"targetLang={mutationTargetLanguage}|typedFreq={dictionary.GetFrequency(mutation, language):F3}|"
                    + $"targetFreq={dictionary.GetFrequency(result.ReplacementToken, mutationTargetLanguage):F3}|"
                    + $"typedAlsoInTarget={dictionary.Contains(mutation, mutationTargetLanguage)}|"
                    + $"spellClass={oracleAnalysis.Class}|srcScript={TokenScriptAnalyzer.Classify(mutation)}|"
                    + $"tgtScript={TokenScriptAnalyzer.Classify(intendedSource)}|"
                    + $"harness=MutationAmbiguous|stage=AmbiguousDirectLayoutApply");
                if (report.PrivacySafeLayoutPairFailureCaseIds.Count < 16)
                {
                    report.PrivacySafeLayoutPairFailureCaseIds.Add(mutationCaseId);
                }
                var layoutSample = new AuditCaseSample
                {
                    Token = mutation,
                    Language = language,
                    IntendedSource = intendedSource,
                    Operation = oracleAnalysis.Operation,
                    Kind = AuditSampleKind.WrongDirectLayout,
                };
                if (report.AuditSamples.Count(sample => sample.Kind == AuditSampleKind.WrongDirectLayout) < 100)
                {
                    report.AuditSamples.Add(layoutSample);
                }

                auditSampler.Consider(layoutSample);
            }
            else
            {
                var reason = DiagnoseAmbiguousAppliedReason(
                    mutation,
                    intendedSource,
                    language,
                    dictionary,
                    options,
                    generatedCandidates,
                    oracleAnalysis,
                    result,
                    gateCallsBefore,
                    gateCallsAfter);
                CorpusAuditAccounting.RecordAmbiguousAppliedReason(report, reason);
                if (reason == AmbiguousAppliedReason.OracleProductionDisagreement)
                {
                    var diff = OracleProductionCandidateSetDiff.Analyze(
                        mutation,
                        language,
                        dictionary,
                        options,
                        oracleAnalysis,
                        result);
                    OracleProductionCandidateSetDiff.RecordDiff(report, diff, oracleAnalysis.Operation);
                    auditSampler.Consider(new AuditCaseSample
                    {
                        Token = mutation,
                        Language = language,
                        IntendedSource = intendedSource,
                        Operation = oracleAnalysis.Operation,
                        Kind = AuditSampleKind.OracleProductionDisagreement,
                    });
                }

                if (reason == AmbiguousAppliedReason.GateNotCalled)
                {
                    report.GateNotCalledCount++;
                }
                else if (reason == AmbiguousAppliedReason.GateCalledButAllowed)
                {
                    report.GateCalledButAllowedCount++;
                }

                CorpusAuditAccounting.RecordAmbiguousTerminal(
                    report,
                    AmbiguousMutationTerminal.AmbiguousSpellingApplied);
                RecordDeveloperAmbiguousAppliedExample(
                    report,
                    mutation,
                    intendedSource,
                    language,
                    dictionary,
                    options,
                    oracleAnalysis,
                    result,
                    reason);
                if (report.AuditSamples.Count(sample => sample.Kind == AuditSampleKind.AmbiguousApplied) < 250)
                {
                    var ambiguousSample = new AuditCaseSample
                    {
                        Token = mutation,
                        Language = language,
                        IntendedSource = intendedSource,
                        Operation = oracleAnalysis.Operation,
                        Kind = AuditSampleKind.AmbiguousApplied,
                    };
                    report.AuditSamples.Add(ambiguousSample);
                    auditSampler.Consider(ambiguousSample);
                }
            }

            report.WrongConfidentCorrections++;
            CountOperation(report.WrongConfidentByOperation, oracleAnalysis.Operation);
            report.AddTerminal(AuditTerminalCategory.WrongConfident);
            CountCluster(report, oracleAnalysis.Cluster);
            AddFailure(
                report,
                $"ambiguousApplied:{mutation}->{result.ReplacementToken} (from {intendedSource}/{oracleAnalysis.Cluster})");
            AddDiagnostic(report, oracleAnalysis.Cluster, mutation, intendedSource, result.ReplacementToken);
            return;
        }

        report.MutationAmbiguousSafelyWaited++;
        report.SafelyWaited++;
        var terminal = result.Recommendation switch
        {
            JointCorrectionRecommendation.Wait => AmbiguousMutationTerminal.AmbiguousSafelyWaited,
            _ => AmbiguousMutationTerminal.AmbiguousNoCandidate,
        };
        CorpusAuditAccounting.RecordAmbiguousTerminal(report, terminal);
        report.AddTerminal(
            result.Recommendation == JointCorrectionRecommendation.Wait
                ? AuditTerminalCategory.AmbiguousWait
                : AuditTerminalCategory.NoCandidate);
    }

    private static void RecordOperationAgreement(
        MaximumCorpusAuditReport report,
        MutationAnalysisResult oracleAnalysis,
        string mutation,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        JointCorrectionDecisionResult result)
    {
        if (result.Recommendation != JointCorrectionRecommendation.Apply
            || result.Kind != CorrectionKind.Autocorrect
            || string.IsNullOrEmpty(result.ReplacementToken))
        {
            return;
        }

        var productionAnalysis = MutationClassificationOracle.Analyze(
            mutation,
            language,
            dictionary,
            options);
        if (productionAnalysis.Operation == oracleAnalysis.Operation)
        {
            report.OracleProductionOperationMatch++;
        }
        else
        {
            report.OracleProductionOperationMismatch++;
        }
    }

    private static AmbiguousAppliedReason DiagnoseAmbiguousAppliedReason(
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        IReadOnlyList<GeneratedAutocorrectionCandidate> generatedCandidates,
        MutationAnalysisResult independentOracle,
        JointCorrectionDecisionResult result,
        int gateCallsBefore,
        int gateCallsAfter)
    {
        if (gateCallsAfter <= gateCallsBefore)
        {
            return AmbiguousAppliedReason.GateNotCalled;
        }

        if (LayoutServiceWordWhitelist.TryGetRussianReplacement(mutation, out _))
        {
            return AmbiguousAppliedReason.WhitelistBypass;
        }

        var productionOracle = MutationClassificationOracle.Analyze(
            mutation,
            language,
            dictionary,
            options);
        if (independentOracle.Class == MutationOracleClass.Ambiguous
            && productionOracle.Class == MutationOracleClass.UniquelyRecoverable)
        {
            return AmbiguousAppliedReason.OracleProductionDisagreement;
        }

        var generatedHasIntended = generatedCandidates.Any(candidate =>
            string.Equals(candidate.Word, intendedSource, StringComparison.OrdinalIgnoreCase));
        var reverseHasIntended = independentOracle.Sources.Any(source =>
            string.Equals(source.Word, intendedSource, StringComparison.OrdinalIgnoreCase));
        if (reverseHasIntended && !generatedHasIntended)
        {
            return AmbiguousAppliedReason.CandidateSetTruncated;
        }

        if (productionOracle.Class == MutationOracleClass.UniquelyRecoverable
            && productionOracle.Operation != independentOracle.Operation)
        {
            return AmbiguousAppliedReason.OperationMismatch;
        }

        if (result.Kind == CorrectionKind.Layout)
        {
            return AmbiguousAppliedReason.LayoutAlternativeMissing;
        }

        if (result.Kind == CorrectionKind.Combined)
        {
            return AmbiguousAppliedReason.CombinedAlternativeMissing;
        }

        return AmbiguousAppliedReason.GateCalledButAllowed;
    }

    private static void CountOperation(Dictionary<EditOperationType, int> map, EditOperationType operation)
    {
        map.TryGetValue(operation, out var count);
        map[operation] = count + 1;
    }

    private static bool HasLayoutLikeShape(string left, string right)
    {
        return TokenScriptAnalyzer.Classify(left) != TokenScriptAnalyzer.Classify(right);
    }

    private static IEnumerable<string> CreateMutations(string word, TypingLanguage language, Random random)
    {
        if (word.Length >= 2)
        {
            var repeatIndex = random.Next(word.Length);
            yield return word.Insert(repeatIndex, word[repeatIndex].ToString());
        }

        if (word.Length >= 4)
        {
            yield return word.Remove(random.Next(word.Length), 1);
        }

        if (word.Length >= 2)
        {
            var buffer = word.ToCharArray();
            var i = random.Next(word.Length - 1);
            (buffer[i], buffer[i + 1]) = (buffer[i + 1], buffer[i]);
            yield return new string(buffer);
        }

        if (language == TypingLanguage.Russian)
        {
            var vowelIndex = IndexOfAny(word, RussianVowels);
            if (vowelIndex >= 0)
            {
                var buffer = word.ToCharArray();
                buffer[vowelIndex] = RussianVowels[random.Next(RussianVowels.Length)];
                yield return new string(buffer);
            }
        }
        else
        {
            var buffer = word.ToCharArray();
            var i = random.Next(word.Length);
            buffer[i] = (char)('a' + random.Next(26));
            yield return new string(buffer);
        }
    }

    private static IEnumerable<string> GenerateCollisionNeighbors(string word, Random random)
    {
        if (word.Length == 0)
        {
            yield break;
        }

        var alphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        yield return word.Remove(word.Length / 2, 1);
        yield return word.Insert(Math.Min(1, word.Length), word[0].ToString());
        if (word.Length >= 2)
        {
            var transposed = word.ToCharArray();
            (transposed[0], transposed[1]) = (transposed[1], transposed[0]);
            yield return new string(transposed);
        }

        var sub = word.ToCharArray();
        sub[random.Next(sub.Length)] = alphabet[random.Next(alphabet.Length)];
        yield return new string(sub);
    }

    private static int IndexOfAny(string word, char[] chars)
    {
        for (var index = 0; index < word.Length; index++)
        {
            if (chars.Contains(word[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static void CountCluster(MaximumCorpusAuditReport report, MutationAmbiguityCluster cluster)
    {
        report.WrongConfidentClusters.TryGetValue(cluster, out var count);
        report.WrongConfidentClusters[cluster] = count + 1;
    }

    private static void RecordDeveloperAmbiguousAppliedExample(
        MaximumCorpusAuditReport report,
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        MutationAnalysisResult oracleAnalysis,
        JointCorrectionDecisionResult result,
        AmbiguousAppliedReason reason)
    {
        var production = MutationClassificationOracle.Analyze(mutation, language, dictionary, options);
        _ = OperationPrecisionGate.TryGetForcingAlternate(
            production,
            out var winner,
            out var longerOrPeer,
            out var discardReason);
        if (string.IsNullOrEmpty(winner.Word))
        {
            winner = production.Sources
                .Where(source => string.Equals(source.Word, production.UniqueTarget, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static source => source.EditCost)
                .ThenByDescending(static source => source.Frequency)
                .FirstOrDefault();
        }

        var intended = production.Sources
            .Where(source => string.Equals(source.Word, intendedSource, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static source => source.EditCost)
            .ThenByDescending(static source => source.Frequency)
            .FirstOrDefault();
        // Prefer intended peer metrics for AA analysis; fall back to forcing R1 alternate.
        var peer = !string.IsNullOrEmpty(intended.Word) ? intended : longerOrPeer;
        var parentIsIntended = !string.IsNullOrEmpty(peer.Word)
            && string.Equals(peer.Word, intendedSource, StringComparison.OrdinalIgnoreCase);
        var inBand = !string.IsNullOrEmpty(winner.Word)
            && !string.IsNullOrEmpty(peer.Word)
            && OperationPrecisionGate.IsCredibleNearCostParent(winner, peer);
        var peerDiscard = !string.IsNullOrEmpty(winner.Word) && !string.IsNullOrEmpty(peer.Word)
            ? OperationPrecisionGate.DiagnoseDiscardReason(winner, peer)
            : discardReason;
        var caseId = AuditCaseHasher.HashCase(mutation, intendedSource, result.ReplacementToken);
        report.DeveloperSyntheticAmbiguousAppliedExamples.Add(
            $"caseId={caseId}|op={oracleAnalysis.Operation}|selected={result.ReplacementToken}|intended={intendedSource}|"
            + $"longerParent={peer.Word}|parentFreq={peer.Frequency:F3}|candFreq={winner.Frequency:F3}|"
            + $"parentCost={peer.EditCost:F2}|candCost={winner.EditCost:F2}|inBand={inBand}|"
            + $"parentIsIntended={parentIsIntended}|discardReason={peerDiscard}|"
            + $"productionVerdict={result.Recommendation}|expectedVerdict=Wait|"
            + $"token={mutation}|uniq={production.UniqueTarget}|intendedInSrc={!string.IsNullOrEmpty(intended.Word)}|"
            + $"parentOp={peer.Operation}|candOp={winner.Operation}|"
            + $"lenParent={peer.Word?.Length ?? 0}|lenCand={winner.Word?.Length ?? 0}|lenTok={mutation.Length}|"
            + $"reason={reason}");
    }

    private static void RecordDeveloperR1ForcedWaitExample(
        MaximumCorpusAuditReport report,
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        MutationAnalysisResult oracleAnalysis,
        JointCorrectionDecisionResult result)
    {
        // Uncapped: recovery-loss analysis needs every Unique==intended Wait.
        var production = MutationClassificationOracle.Analyze(mutation, language, dictionary, options);
        if (!OperationPrecisionGate.HasDiscardedCredibleCompetitor(production))
        {
            return;
        }

        OperationPrecisionGate.TryGetForcingAlternate(
            production,
            out var winner,
            out var longerOrPeer,
            out var discardReason);
        var parentIsIntended = !string.IsNullOrEmpty(longerOrPeer.Word)
            && string.Equals(longerOrPeer.Word, intendedSource, StringComparison.OrdinalIgnoreCase);
        var inBand = !string.IsNullOrEmpty(winner.Word)
            && !string.IsNullOrEmpty(longerOrPeer.Word)
            && OperationPrecisionGate.IsCredibleNearCostParent(winner, longerOrPeer);
        var oneEdit = !string.IsNullOrEmpty(winner.Word)
            && !string.IsNullOrEmpty(longerOrPeer.Word)
            && OperationPrecisionGate.IsGenuineOneEditExplanation(winner, longerOrPeer);
        var blockGroup = !string.IsNullOrEmpty(winner.Word) && !string.IsNullOrEmpty(longerOrPeer.Word)
            ? OperationPrecisionGate.ClassifyRecoveryBlockGroup(winner, longerOrPeer)
            : "other";
        var lengthRelation = longerOrPeer.Word.Length > winner.Word.Length
            ? "comp_longer"
            : longerOrPeer.Word.Length == winner.Word.Length
                ? "same_length"
                : "comp_shorter";
        var caseId = AuditCaseHasher.HashCase(mutation, intendedSource, production.UniqueTarget);
        report.DeveloperSyntheticR1ForcedWaitExamples.Add(
            $"caseId={caseId}|op={oracleAnalysis.Operation}|selected={production.UniqueTarget}|intended={intendedSource}|"
            + $"blockingRule={blockGroup}|discardReason={discardReason}|"
            + $"competitor={longerOrPeer.Word}|compFreq={longerOrPeer.Frequency:F3}|candFreq={winner.Frequency:F3}|"
            + $"compCost={longerOrPeer.EditCost:F2}|candCost={winner.EditCost:F2}|"
            + $"lengthRelation={lengthRelation}|oneEditComp={oneEdit}|inBand={inBand}|"
            + $"parentIsIntended={parentIsIntended}|"
            + $"productionVerdict={result.Recommendation}|expectedVerdict=Apply|"
            + $"token={mutation}|parentOp={longerOrPeer.Operation}|candOp={winner.Operation}");
    }

    private static void RecordDeveloperLayoutPairFailure(
        MaximumCorpusAuditReport report,
        string typed,
        string expectedTarget,
        TypingLanguage typedLanguage,
        TypingLanguage targetLanguage,
        string policyVerdict,
        string expectedVerdict,
        JointCorrectionDecisionResult result,
        IAutocorrectDictionary dictionary,
        string harnessKind)
    {
        var caseId = AuditCaseHasher.HashCase(typed, expectedTarget, result.ReplacementToken);
        var typedFreq = dictionary.GetFrequency(typed, typedLanguage);
        var targetFreq = dictionary.GetFrequency(expectedTarget, targetLanguage);
        var typedAlsoTarget = dictionary.Contains(typed, targetLanguage);
        var spelling = MutationClassificationOracle.Analyze(
            typed,
            typedLanguage,
            dictionary,
            new AutocorrectionOptions());
        report.DeveloperSyntheticLayoutPairFailures.Add(
            $"caseId={caseId}|typed={typed}|expected={expectedTarget}|actual={result.ReplacementToken}|"
            + $"policy={policyVerdict}|expectedVerdict={expectedVerdict}|actualVerdict={result.Recommendation}/{result.Kind}|"
            + $"typedLang={typedLanguage}|targetLang={targetLanguage}|"
            + $"typedFreq={typedFreq:F3}|targetFreq={targetFreq:F3}|typedAlsoInTarget={typedAlsoTarget}|"
            + $"spellClass={spelling.Class}|spellUniq={spelling.UniqueTarget}|spellOp={spelling.Operation}|"
            + $"srcScript={TokenScriptAnalyzer.Classify(typed)}|tgtScript={TokenScriptAnalyzer.Classify(expectedTarget)}|"
            + $"harness={harnessKind}|stage=LayoutPairPolicy");
    }

    private static void AddFailure(MaximumCorpusAuditReport report, string sample)
    {
        if (report.FailureSamples.Count < 60)
        {
            // Never persist raw tokens — store a stable hash of the diagnostic payload.
            report.FailureSamples.Add(AuditCaseHasher.HashCase(sample, string.Empty, null));
        }
    }

    internal static void AddDiagnostic(
        MaximumCorpusAuditReport report,
        MutationAmbiguityCluster cluster,
        string mutation,
        string source,
        string? replacement)
    {
        if (report.DiagnosticClusterSamples.Count >= 200)
        {
            return;
        }

        var caseId = AuditCaseHasher.HashCase(mutation, source, replacement);
        var language = TokenScriptAnalyzer.Classify(mutation) == TokenScript.Cyrillic
            ? TypingLanguage.Russian
            : TypingLanguage.English;
        var operation = EditOperationClassifier.Classify(mutation, source, language);
        report.DiagnosticClusterSamples.Add($"{cluster}|{caseId}|{operation}|meta");
    }

    private static void WriteDiagnosticArtifact(MaximumCorpusAuditReport report)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "SmartInputAudit");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"wrong-confident-clusters-seed{report.Seed}.tsv");
            var lines = new List<string> { "cluster\tcaseId\toperation\tmeta" };
            lines.AddRange(report.DiagnosticClusterSamples.Select(sample => sample.Replace('|', '\t')));
            foreach (var pair in report.WrongConfidentClusters.OrderByDescending(static entry => entry.Value))
            {
                lines.Add($"COUNT\t{pair.Key}\t{pair.Value}\t");
            }

            File.WriteAllLines(path, lines);
        }
        catch
        {
            // Diagnostic artifact is best-effort for local audit only.
        }
    }

    private static void FinalizeDurations(MaximumCorpusAuditReport report, List<double> durations)
    {
        if (durations.Count == 0)
        {
            return;
        }

        // Bound memory: keep a reservoir for percentile estimation instead of
        // retaining every mutation's latency for the full audit lifetime.
        const int maxSamples = 8_192;
        if (durations.Count > maxSamples)
        {
            var random = new Random(report.Seed ^ unchecked((int)report.ElapsedMs));
            for (var index = durations.Count - 1; index >= maxSamples; index--)
            {
                var swap = random.Next(index + 1);
                (durations[index], durations[swap]) = (durations[swap], durations[index]);
            }

            durations.RemoveRange(maxSamples, durations.Count - maxSamples);
        }

        durations.Sort();
        report.AverageMs = durations.Average();
        report.P95Ms = durations[(int)(durations.Count * 0.95)];
        report.P99Ms = durations[(int)(durations.Count * 0.99)];
        report.MaxMs = durations[^1];
    }

    private static string ComputeCorpusFingerprint()
    {
        var material = $"{StarterAutocorrectLexicon.EnglishWordCount}:{StarterAutocorrectLexicon.RussianWordCount}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..12];
    }
}
