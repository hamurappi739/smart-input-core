using SmartInput.Core.Engines;
using SmartInput.Core.Integration;

namespace SmartInput.Core.Tests;

public sealed class AdditionalCorrectionScoringTests
{
    [Fact]
    public void Compare_IdenticalApply_IsAuditOnlyAndUsesDigests()
    {
        var core = CreateEngine();
        var audit = new AdditionalCorrectionScoringAuditService(core);
        var result = audit.Compare(
            new PortableCorrectionRequest { Token = "ghbdtn", AutocorrectEnabled = false },
            new FixedScorer(new AdditionalCorrectionScore("apply", "привет", 0.99, "layout")));

        Assert.Equal(AdditionalScorerComparisonOutcome.CoreApplyScorerApplySame, result.Outcome);
        Assert.Equal("apply", result.CoreDecision);
        Assert.NotNull(result.CoreReplacementDigest);
        Assert.NotNull(result.ScorerReplacementDigest);
        Assert.DoesNotContain("привет", result.CoreReplacementDigest!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("привет", result.ScorerReplacementDigest!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_ScorerApplyNeverChangesCoreDecision()
    {
        var audit = new AdditionalCorrectionScoringAuditService(CreateEngine());
        var result = audit.Compare(
            new PortableCorrectionRequest { Token = "https://example.com/ghbdtn" },
            new FixedScorer(new AdditionalCorrectionScore("apply", "привет", 1, "layout")));

        Assert.Equal("no_change", result.CoreDecision);
        Assert.Equal(AdditionalScorerComparisonOutcome.CoreNoChangeScorerApply, result.Outcome);
    }

    [Fact]
    public void Compare_InvalidScorerResult_IsRejected()
    {
        var audit = new AdditionalCorrectionScoringAuditService(CreateEngine());
        var result = audit.Compare(
            new PortableCorrectionRequest { Token = "привет" },
            new FixedScorer(new AdditionalCorrectionScore("apply", null, 2, "spelling")));

        Assert.Equal(AdditionalScorerComparisonOutcome.InvalidScorerResult, result.Outcome);
        Assert.Equal("no_change", result.CoreDecision);
    }

    private static IPortableCorrectionEngine CreateEngine()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        var decision = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(),
            converter);
        return new PortableCorrectionEngine(decision, dictionary);
    }

    private sealed class FixedScorer(AdditionalCorrectionScore score) : IAdditionalCorrectionScorer
    {
        public AdditionalCorrectionScore Evaluate(PortableCorrectionRequest request) => score;
    }
}
