using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

internal enum UniqueMutationTerminal
{
    CorrectRecovery,
    WrongUniqueTarget,
    WaitedUnique,
    NoCandidateUnique,
    BlockedUnique,
    ProtectedUnique,
    UnsupportedUnique,
    FailedUnique,
    ExceptionUnique,
    CancelledUnique,
    TimedOutUnique,
}

internal enum AmbiguousAppliedReason
{
    GateNotCalled,
    GateCalledButAllowed,
    CandidateSetTruncated,
    ReverseIndexMiss,
    OperationMismatch,
    LanguageMismatch,
    EditCostMismatch,
    FrequencyBandMismatch,
    LayoutAlternativeMissing,
    CombinedAlternativeMissing,
    WhitelistBypass,
    PreparedCorrectionBypass,
    OracleProductionDisagreement,
}

internal enum AmbiguousMutationTerminal
{
    AmbiguousSafelyWaited,
    DominanceAccepted,
    ExplicitLayoutAnchorAccepted,
    AmbiguousNoCandidate,
    AmbiguousBlocked,
    AmbiguousSpellingApplied,
    AmbiguousDirectLayoutApplied,
    AmbiguousCombinedApplied,
    AmbiguousUnknownNameApplied,
    AmbiguousPreparedLayoutApplied,
    AmbiguousWhitelistApplied,
    AmbiguousFailed,
}

internal enum OracleProductionDisagreementReason
{
    ProductionSignatureIndexMiss,
    CandidateGenerationTruncation,
    OperationDefinitionMismatch,
    EditCostMismatch,
    FrequencyNormalizationMismatch,
    DictionaryTierMismatch,
    MorphologyEvidenceMismatch,
    CrossOperationCompetitorMissing,
    DirectLayoutCompetitorMissing,
    CombinedCompetitorMissing,
    UnknownNameCompetitorMissing,
    CaseNormalizationMismatch,
    YeYoNormalizationMismatch,
    Unclassified,
}

internal static class CorpusAuditAccounting
{
    internal static void RecordUniqueTerminal(
        MaximumCorpusAuditReport report,
        UniqueMutationTerminal terminal)
    {
        report.UniqueTerminalCounts.TryGetValue(terminal, out var count);
        report.UniqueTerminalCounts[terminal] = count + 1;
    }

    internal static void RecordAmbiguousTerminal(
        MaximumCorpusAuditReport report,
        AmbiguousMutationTerminal terminal)
    {
        report.AmbiguousTerminalCounts.TryGetValue(terminal, out var count);
        report.AmbiguousTerminalCounts[terminal] = count + 1;
    }

    internal static void RecordDisagreementReason(
        MaximumCorpusAuditReport report,
        OracleProductionDisagreementReason reason)
    {
        report.DisagreementReasonCounts.TryGetValue(reason, out var count);
        report.DisagreementReasonCounts[reason] = count + 1;
    }

    internal static int SumUniqueTerminals(MaximumCorpusAuditReport report)
        => report.UniqueTerminalCounts.Values.Sum();

    internal static int SumAmbiguousNonAppliedTerminals(MaximumCorpusAuditReport report)
        => report.AmbiguousTerminalCounts
            .Where(pair => pair.Key is not (
                AmbiguousMutationTerminal.DominanceAccepted
                or
                AmbiguousMutationTerminal.AmbiguousSpellingApplied
                or AmbiguousMutationTerminal.AmbiguousDirectLayoutApplied
                or AmbiguousMutationTerminal.AmbiguousCombinedApplied
                or AmbiguousMutationTerminal.AmbiguousUnknownNameApplied
                or AmbiguousMutationTerminal.AmbiguousPreparedLayoutApplied
                or AmbiguousMutationTerminal.AmbiguousWhitelistApplied))
            .Sum(pair => pair.Value);

    internal static int SumAmbiguousTerminals(MaximumCorpusAuditReport report)
        => report.AmbiguousTerminalCounts.Values.Sum();

    internal static void RecordAmbiguousAppliedReason(
        MaximumCorpusAuditReport report,
        AmbiguousAppliedReason reason)
    {
        report.AmbiguousAppliedReasonCounts.TryGetValue(reason, out var count);
        report.AmbiguousAppliedReasonCounts[reason] = count + 1;
    }

    internal static int ComputeTotalAmbiguousApplied(MaximumCorpusAuditReport report)
        => report.AmbiguousSpellingApplied
            + report.AmbiguousDirectLayoutApplied
            + report.AmbiguousCombinedApplied
            + report.AmbiguousUnknownNameApplied
            + report.AmbiguousPreparedLayoutApplied
            + report.AmbiguousWhitelistApplied;

    internal static int ComputeMissedRecoveries(MaximumCorpusAuditReport report)
        => report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.WaitedUnique)
            + report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.NoCandidateUnique)
            + report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.BlockedUnique)
            + report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.ProtectedUnique)
            + report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.UnsupportedUnique)
            + report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.FailedUnique)
            + report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.CancelledUnique)
            + report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.TimedOutUnique);

    internal static void SyncCanonicalCounters(MaximumCorpusAuditReport report)
    {
        report.AmbiguousSpellingApplied = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousSpellingApplied);
        report.AmbiguousDirectLayoutApplied = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousDirectLayoutApplied);
        report.AmbiguousCombinedApplied = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousCombinedApplied);
        report.AmbiguousUnknownNameApplied = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousUnknownNameApplied);
        report.AmbiguousPreparedLayoutApplied = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousPreparedLayoutApplied);
        report.AmbiguousWhitelistApplied = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousWhitelistApplied);
        report.TotalAmbiguousApplied = ComputeTotalAmbiguousApplied(report);
        report.AmbiguousAppliedBeforeAccountingFix = report.AmbiguousSpellingApplied;
        report.AmbiguousSafelyWaited = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousSafelyWaited);
        report.AmbiguousDominanceAccepted = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.DominanceAccepted);
        report.AmbiguousLayoutAnchorAccepted = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.ExplicitLayoutAnchorAccepted);
        report.AmbiguousNoCandidate = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousNoCandidate);
        report.AmbiguousBlocked = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousBlocked);
        report.AmbiguousFailed = report.AmbiguousTerminalCounts.GetValueOrDefault(
            AmbiguousMutationTerminal.AmbiguousFailed);
        report.OracleProductionDisagreementCount = report.DisagreementReasonCounts.Values.Sum();
        report.TokenLevelMutationDecisions = report.RussianMutationsEvaluated + report.EnglishMutationsEvaluated;
    }

    internal static void AssertAllReconciliations(MaximumCorpusAuditReport report)
    {
        SyncCanonicalCounters(report);
        report.ReconcileWrongConfidentTotal();
        report.SyncLegacyMissedFromTerminals();

        var missedFromTerminals = ComputeMissedRecoveries(report);
        if (report.MissedRecoveries != missedFromTerminals)
        {
            throw new InvalidOperationException(
                $"MissedRecoveries mismatch: aggregate={report.MissedRecoveries} terminalSum={missedFromTerminals}");
        }

        if (report.OracleUniquelyRecoverable != SumUniqueTerminals(report))
        {
            throw new InvalidOperationException(
                $"Unique oracle reconciliation failed: oracle={report.OracleUniquelyRecoverable} "
                + $"terminals={SumUniqueTerminals(report)}");
        }

        var canonicalAmbiguousSum = report.AmbiguousSafelyWaited
            + report.AmbiguousDominanceAccepted
            + report.AmbiguousLayoutAnchorAccepted
            + report.TotalAmbiguousApplied
            + report.AmbiguousBlocked
            + report.AmbiguousFailed
            + report.AmbiguousNoCandidate;
        if (report.OracleAmbiguous != canonicalAmbiguousSum)
        {
            throw new InvalidOperationException(
                $"Ambiguous canonical reconciliation failed: oracle={report.OracleAmbiguous} "
                + $"safelyWaited={report.AmbiguousSafelyWaited} totalApplied={report.TotalAmbiguousApplied} "
                + $"layoutAnchorAccepted={report.AmbiguousLayoutAnchorAccepted} "
                + $"blocked={report.AmbiguousBlocked} failed={report.AmbiguousFailed} "
                + $"noCandidate={report.AmbiguousNoCandidate} sum={canonicalAmbiguousSum}");
        }

        if (ComputeTotalAmbiguousApplied(report) != report.TotalAmbiguousApplied)
        {
            throw new InvalidOperationException("TotalAmbiguousApplied internal sum mismatch.");
        }

        var appliedTerminalSum = report.AmbiguousSpellingApplied
            + report.AmbiguousDirectLayoutApplied
            + report.AmbiguousCombinedApplied
            + report.AmbiguousUnknownNameApplied
            + report.AmbiguousPreparedLayoutApplied
            + report.AmbiguousWhitelistApplied;
        if (appliedTerminalSum != report.TotalAmbiguousApplied)
        {
            throw new InvalidOperationException(
                $"TotalAmbiguousApplied mismatch: canonical={report.TotalAmbiguousApplied} "
                + $"terminalSum={appliedTerminalSum}");
        }

        if (report.TotalMutations != report.OracleUniquelyRecoverable + report.OracleAmbiguous
            + report.OracleExactKnown + report.OracleInvalidOrProtected)
        {
            throw new InvalidOperationException("Total mutation class reconciliation failed.");
        }

        if (report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.ExceptionUnique) > 0)
        {
            throw new InvalidOperationException(
                $"ExceptionUnique={report.UniqueTerminalCounts[UniqueMutationTerminal.ExceptionUnique]}");
        }

        if (report.DisagreementReasonCounts.Values.Sum() > 0
            && report.DisagreementReasonCounts.Values.Sum() != report.AmbiguousSpellingApplied)
        {
            throw new InvalidOperationException(
                $"Disagreement reason buckets ({report.DisagreementReasonCounts.Values.Sum()}) "
                + $"!= AmbiguousSpellingApplied ({report.AmbiguousSpellingApplied})");
        }
    }

    internal static string FormatAccountingSummary(MaximumCorpusAuditReport report)
    {
        SyncCanonicalCounters(report);
        var uniqueBreakdown = string.Join(
            ",",
            report.UniqueTerminalCounts.OrderBy(static pair => pair.Key)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        var ambiguousBreakdown = string.Join(
            ",",
            report.AmbiguousTerminalCounts.OrderBy(static pair => pair.Key)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        var reasonBreakdown = string.Join(
            ",",
            report.DisagreementReasonCounts.OrderBy(static pair => pair.Key)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        return
            $"uniqueTerminals=[{uniqueBreakdown}]; ambiguousTerminals=[{ambiguousBreakdown}]; "
            + $"totalAmbiguousApplied={report.TotalAmbiguousApplied} "
            + $"(spelling={report.AmbiguousSpellingApplied},layout={report.AmbiguousDirectLayoutApplied},"
            + $"combined={report.AmbiguousCombinedApplied}); "
            + $"tokenLevelDecisions={report.TokenLevelMutationDecisions}; "
            + $"candidateGateCalls={report.ApplyGateCallCount}; gateAllowed={report.ApplyGateAllowedCount}; "
            + $"gateDenied={report.ApplyGateDeniedCount}; gateNotCalled={report.GateNotCalledCount}; "
            + $"disagreementReasons=[{reasonBreakdown}]; "
            + $"setDiff={report.OracleTargetsMissingFromProduction}/{report.ProductionTargetsMissingFromOracle}/"
            + $"{report.TargetsPresentWithDifferentOperation}; "
            + $"bruteForce={report.BruteForceCasesEvaluated} "
            + $"bfOracle={report.BruteForceOracleAgreements}/{report.BruteForceOracleDisagreements} "
            + $"bfProduction={report.BruteForceProductionAgreements}/{report.BruteForceProductionDisagreements}; "
            + $"bfDisagreementReasons=[{string.Join(",", report.BruteForceOracleDisagreementReasons.OrderBy(static p => p.Key).Select(p => $"{p.Key}={p.Value}"))}]; "
            + $"mandatory={report.MandatoryRegressionPassed}/{report.MandatoryRegressionTotal}; "
            + $"indexMiss={report.ProductionSignatureIndexMiss}; "
            + $"indexMissBySig=[{string.Join(",", report.IndexMissesBySignature.OrderBy(static p => p.Key).Select(p => $"{p.Key}={p.Value}"))}]; "
            + $"indexLookupAvgCompetitors={report.IndexAverageCompetitorsReturned:F2}; "
            + $"indexLookupMaxCompetitors={report.IndexMaxCompetitorsReturned}; "
            + $"indexLookupP95Ms={report.IndexLookupP95Ms:F3}; indexLookupP99Ms={report.IndexLookupP99Ms:F3}; "
            + $"indexLookupMaxMs={report.IndexLookupMaxMs:F3}; indexMemoryBytes={report.IndexMemoryEstimateBytes}; "
            + $"peakWorkingSetBytes={report.PeakWorkingSetBytes}; "
            + $"opCross=[{OperationDisagreementCrossTable.Format(report)}]; "
            + $"unaccounted={report.UnaccountedTokens}; doubleCounted={report.DoubleCountedTokens}";
    }
}
