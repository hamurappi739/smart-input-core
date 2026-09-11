using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

/// <summary>
/// Seed-42 sample gate after narrow NonR1 Apply recovery.
/// </summary>
[Trait("Category", "FocusedCorrection")]
public class Seed42RecoveryGateTests
{
    private const double SafetyRecoveryFloor = 0.90;

    [Fact]
    public void Seed42_UniqueRecovery_AtLeast90_WithZeroErrorCounters()
    {
        var report = MaximumCorpusAuditHarness.RunFullAudit(
            seed: 42,
            minRussianMutations: 12_000,
            minEnglishMutations: 12_000);

        var mandatory = MandatoryRegressionRunner.EvaluateAll(
            CreateJoint(),
            LiveCorrectionTestHelpers.CreateStarterDictionary());

        Console.WriteLine($"AmbiguousApplied={report.TotalAmbiguousApplied}");
        Console.WriteLine($"WrongUniqueTarget={report.WrongUniqueTarget}");
        Console.WriteLine($"WrongDirectLayout={report.WrongDirectLayout}");
        Console.WriteLine($"WrongCombined={report.WrongCombined}");
        Console.WriteLine($"ExactWordsChanged={report.ExactWordsChanged}");
        Console.WriteLine(
            $"unique recovery={report.RecoveryRate:P4} "
            + $"({report.CorrectMutationRecoveries}/{report.EligibleUnambiguousMutations})");
        Console.WriteLine($"mandatory={mandatory.Passed}/{MandatoryRegressionCatalog.TotalCount}");

        Assert.True(
            report.TotalAmbiguousApplied == 0,
            $"AmbiguousApplied={report.TotalAmbiguousApplied}; "
            + string.Join(
                "; ",
                report.DeveloperSyntheticLayoutPairFailures
                    .Where(entry => entry.Contains(
                        "stage=AmbiguousDirectLayoutApply",
                        StringComparison.Ordinal))
                    .Take(12)));
        Assert.Equal(0, report.WrongUniqueTarget);
        Assert.Equal(0, report.WrongDirectLayout);
        Assert.Equal(0, report.WrongCombined);
        Assert.Equal(0, report.ExactWordsChanged);
        Assert.True(report.RecoveryRate >= SafetyRecoveryFloor);
        Assert.True(mandatory.Failed == 0 && mandatory.Passed >= 104);
    }

    private static JointCorrectionDecisionService CreateJoint()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        return new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon(converter)),
            converter);
    }
}
