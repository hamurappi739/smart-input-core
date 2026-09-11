using System.Security.Cryptography;
using System.Text;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public sealed class RustShadowAuditServiceTests
{
    [Fact]
    public void CompareForTesting_MatchingApply_UsesDigestsInsteadOfText()
    {
        var service = new RustShadowAuditService(new FixedProvider(new RustShadowCandidateResult(
            RustShadowProviderState.Available,
            RustShadowDecision.Replace,
            RustShadowReason.Layout,
            0.98,
            4.2,
            "привет")));

        var result = service.CompareForTesting(
            "ghbdtn",
            "ghbdtn ",
            Apply("ghbdtn", "привет", CorrectionKind.Layout));

        Assert.Equal(RustShadowComparisonOutcome.RustMatchesOwnApply, result.Outcome);
        Assert.Equal("Apply", result.OwnDecision);
        Assert.NotEqual("привет", result.OwnReplacementDigest);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("привет"))),
            result.OwnReplacementDigest);
        Assert.Equal(result.OwnReplacementDigest, result.RustReplacementDigest);
    }

    [Fact]
    public void Observe_OnlyUpdatesAggregateCounters_AndCannotChangeOwnDecision()
    {
        var service = new RustShadowAuditService(new FixedProvider(new RustShadowCandidateResult(
            RustShadowProviderState.Available,
            RustShadowDecision.Replace,
            RustShadowReason.Spelling,
            0.99,
            1.2,
            "другое")));
        var own = Apply("исходное", "правильное", CorrectionKind.Autocorrect);

        service.Observe("исходное", "контекст ", own);

        var status = service.Status;
        Assert.Equal(1, status.Observed);
        Assert.Equal(1, status.DifferingApplies);
        Assert.Equal(0, status.MatchingApplies);
        Assert.Equal("правильное", own.ReplacementToken);
    }

    [Fact]
    public void Observe_WhenProviderIsDisabled_RecordsAvailabilityOnly()
    {
        var service = new RustShadowAuditService(new NullRustShadowCandidateProvider());

        service.Observe("token", "token ", JointCorrectionDecisionResult.NoChange("token"));

        var status = service.Status;
        Assert.Equal(1, status.Observed);
        Assert.Equal(1, status.ProviderUnavailable);
        Assert.Equal(0, status.NativeFailures);
        Assert.Equal(0, status.BothKeep);
    }

    [Fact]
    public void Status_ExposesProviderStateWithoutExposingAnyText()
    {
        var service = new RustShadowAuditService(new NullRustShadowCandidateProvider(
            RustShadowProviderState.AbiMismatch));

        Assert.Equal(RustShadowProviderState.AbiMismatch, service.ProviderState);
        Assert.Equal(0, service.Status.Observed);
    }

    [Fact]
    public void Observe_RecordsOnlyAggregateKindsReasonsAndLatency()
    {
        var service = new RustShadowAuditService(new FixedProvider(new RustShadowCandidateResult(
            RustShadowProviderState.Available,
            RustShadowDecision.Replace,
            RustShadowReason.Layout,
            0.99,
            0.30,
            "привет")));

        service.Observe("ghbdtn", "", Apply("ghbdtn", "привет", CorrectionKind.Layout));

        var status = service.Status;
        Assert.Equal(1, status.OwnLayoutApplies);
        Assert.Equal(1, status.RustLayoutReplacements);
        Assert.Equal(1, status.CandidatesForHumanReview);
        Assert.True(status.TotalEvaluationMicroseconds >= 0);
        Assert.True(status.MaxEvaluationMicroseconds >= 0);
    }

    [Fact]
    public void Observe_WaitAndLowConfidenceKeep_AreClassifiedWithoutText()
    {
        var service = new RustShadowAuditService(new FixedProvider(new RustShadowCandidateResult(
            RustShadowProviderState.Available,
            RustShadowDecision.Keep,
            RustShadowReason.LowConfidence,
            0.42,
            0.01)));

        service.Observe("synthetic", "", JointCorrectionDecisionResult.Wait("synthetic"));

        var status = service.Status;
        Assert.Equal(1, status.OwnWaits);
        Assert.Equal(1, status.RustLowConfidenceKeeps);
        Assert.Equal(0, status.CandidatesForHumanReview);
    }

    [Theory]
    [InlineData(CorrectionKind.Layout, RustShadowReason.Layout, 0.99, 0.30, RustShadowPromotionDisposition.CandidateForHumanReview)]
    [InlineData(CorrectionKind.Autocorrect, RustShadowReason.Spelling, 0.99, 0.30, RustShadowPromotionDisposition.CandidateForHumanReview)]
    [InlineData(CorrectionKind.Combined, RustShadowReason.Layout, 0.99, 0.30, RustShadowPromotionDisposition.NotEligible)]
    [InlineData(CorrectionKind.Layout, RustShadowReason.Spelling, 0.99, 0.30, RustShadowPromotionDisposition.NotEligible)]
    [InlineData(CorrectionKind.Layout, RustShadowReason.Layout, 0.97, 0.30, RustShadowPromotionDisposition.NotEligible)]
    public void PromotionPolicy_IsStrictAndReviewOnly(
        CorrectionKind ownKind,
        RustShadowReason rustReason,
        double confidence,
        double margin,
        RustShadowPromotionDisposition expected)
    {
        var rust = new RustShadowCandidateResult(
            RustShadowProviderState.Available,
            RustShadowDecision.Replace,
            rustReason,
            confidence,
            margin,
            "target");

        var result = RustShadowAuditService.DeterminePromotionDisposition(
            rust,
            Apply("source", "target", ownKind),
            ownApplies: true);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CompareForTesting_RustApplyWhenOwnWait_IsClassifiedWithoutExposingToken()
    {
        var service = new RustShadowAuditService(new FixedProvider(new RustShadowCandidateResult(
            RustShadowProviderState.Available,
            RustShadowDecision.Replace,
            RustShadowReason.Layout,
            0.96,
            4.0,
            "кандидат")));

        var result = service.CompareForTesting(
            "сырой",
            "",
            JointCorrectionDecisionResult.Wait("сырой"));

        Assert.Equal(RustShadowComparisonOutcome.RustAppliesOwnKeeps, result.Outcome);
        Assert.Null(result.OwnReplacementDigest);
        Assert.NotNull(result.RustReplacementDigest);
        Assert.NotEqual("кандидат", result.RustReplacementDigest);
    }

    private static JointCorrectionDecisionResult Apply(string source, string replacement, CorrectionKind kind) =>
        new()
        {
            OriginalToken = source,
            ReplacementToken = replacement,
            Recommendation = JointCorrectionRecommendation.Apply,
            Kind = kind,
            ConfidenceScore = 0.95,
        };

    private sealed class FixedProvider : IRustShadowCandidateProvider
    {
        private readonly RustShadowCandidateResult _result;

        public FixedProvider(RustShadowCandidateResult result) => _result = result;

        public RustShadowProviderState State => _result.ProviderState;

        public RustShadowCandidateResult Evaluate(string token, string context) => _result;
    }
}
