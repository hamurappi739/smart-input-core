using SmartInput.Core.Tests;



namespace SmartInput.Core.Tests;



[Trait("Category", "CorpusBaseline")]

public class CorpusAuditBaselineTests

{

    [Fact]

    public void Seed42_PrintOracleBaseline()

    {

        var report = MaximumCorpusAuditHarness.RunFullAudit(42, 125_000, 125_000);

        var operationWrong = string.Join(

            ",",

            report.WrongConfidentByOperation.OrderBy(static pair => pair.Key)

                .Select(pair => $"{pair.Key}={pair.Value}"));

        var operationUnique = string.Join(

            ",",

            report.UniquelyRecoverableByOperation.OrderBy(static pair => pair.Key)

                .Select(pair => $"{pair.Key}={pair.Value}"));



        Console.WriteLine(report.FormatSummary());

        Console.WriteLine($"wrongByOperation=[{operationWrong}]");

        Console.WriteLine($"uniqueByOperation=[{operationUnique}]");

        Console.WriteLine(report.FormatAccountingSummary());

        Console.WriteLine(

            $"reconcile={report.OracleUniquelyRecoverable + report.OracleAmbiguous + report.OracleExactKnown + report.OracleInvalidOrProtected} "

            + $"vs totalMut={report.TotalMutations}");

        Console.WriteLine(

            $"reconcileUnique={CorpusAuditAccounting.SumUniqueTerminals(report)} "

            + $"vs eligible={report.OracleUniquelyRecoverable}");

        Console.WriteLine(

            $"reconcileAmbiguous={CorpusAuditAccounting.SumAmbiguousTerminals(report)} "

            + $"vs ambiguous={report.OracleAmbiguous}");

        Console.WriteLine(

            $"totalAmbiguousApplied={report.TotalAmbiguousApplied} (beforeFix={report.AmbiguousAppliedBeforeAccountingFix}); "
            + $"wrongUnique={report.WrongUniqueTarget} (beforeFix={report.WrongUniqueTargetBeforeAccountingFix}); "

            + $"wrongLayout={report.WrongDirectLayout}; wrongCombined={report.WrongCombined}");



        Assert.True(report.TotalMutations == 250_000);

        Assert.Equal(report.OracleUniquelyRecoverable, CorpusAuditAccounting.SumUniqueTerminals(report));

        Assert.Equal(report.OracleAmbiguous, CorpusAuditAccounting.SumAmbiguousTerminals(report));

        Assert.Equal(

            report.TotalMutations,

            report.OracleUniquelyRecoverable

            + report.OracleAmbiguous

            + report.OracleExactKnown

            + report.OracleInvalidOrProtected);

        Assert.Equal(0, report.UnaccountedTokens);

        Assert.Equal(0, report.DoubleCountedTokens);

        Assert.Equal(0, report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.ExceptionUnique));

        CorpusAuditAccounting.AssertAllReconciliations(report);
    }

}


