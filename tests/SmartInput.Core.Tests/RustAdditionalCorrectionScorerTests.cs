using SmartInput.Core.Integration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Rust;

namespace SmartInput.Core.Tests;

public sealed class RustAdditionalCorrectionScorerTests
{
    [Fact]
    public void ContextFormatter_ContainsOnlyAggregateLanguageStatistics()
    {
        var context = RustShadowContextFormatter.FromHint(
            new SentenceLanguageHint(TypingLanguage.Russian, 2, 1, 3, 17));

        Assert.Equal("lang=ru;ru=2;en=1;tokens=3;chars=17", context);
        Assert.DoesNotContain("previous", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_MapsRustApplyToGenericAuditContract()
    {
        var scorer = new RustAdditionalCorrectionScorer(new FixedProvider(
            new RustShadowCandidateResult(
                RustShadowProviderState.Available,
                RustShadowDecision.Replace,
                RustShadowReason.Spelling,
                1.4,
                0.4,
                "привет")));

        var result = scorer.Evaluate(new PortableCorrectionRequest
        {
            Token = "превет",
            DominantLanguage = "ru",
            RussianTokenCount = 4,
            EnglishTokenCount = 0,
            ContextTokenCount = 4,
            ContextCharacterCount = 20,
        });

        Assert.Equal("apply", result.Decision);
        Assert.Equal("привет", result.ReplacementToken);
        Assert.Equal(1, result.ConfidenceScore);
        Assert.Equal("spelling", result.Kind);
    }

    [Fact]
    public void Evaluate_KeepLowConfidenceBecomesWait()
    {
        var scorer = new RustAdditionalCorrectionScorer(new FixedProvider(
            new RustShadowCandidateResult(
                RustShadowProviderState.Available,
                RustShadowDecision.Keep,
                RustShadowReason.LowConfidence,
                0.25,
                0)));

        var result = scorer.Evaluate(new PortableCorrectionRequest { Token = "неясно" });

        Assert.Equal("wait", result.Decision);
        Assert.Null(result.ReplacementToken);
    }

    [Fact]
    public void Evaluate_UnavailableProviderFailsClosed()
    {
        var scorer = new RustAdditionalCorrectionScorer(new FixedProvider(
            RustShadowCandidateResult.Unavailable(RustShadowProviderState.AbiMismatch)));

        var result = scorer.Evaluate(new PortableCorrectionRequest { Token = "ghbdtn" });

        Assert.Equal("wait", result.Decision);
        Assert.Equal("provider_unavailable", result.Kind);
        Assert.Null(result.ReplacementToken);
    }

    private sealed class FixedProvider(RustShadowCandidateResult result) : IRustShadowCandidateProvider
    {
        public RustShadowProviderState State => result.ProviderState;

        public RustShadowCandidateResult Evaluate(string token, string context)
        {
            Assert.Contains("lang=", context, StringComparison.Ordinal);
            Assert.DoesNotContain("previous", context, StringComparison.OrdinalIgnoreCase);
            return result;
        }
    }
}
