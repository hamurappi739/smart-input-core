using SmartInput.Core.Configuration;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public sealed class RustHybridCorrectionServiceTests
{
    [Fact]
    public void RustSpellingCandidate_IsAcceptedOnlyThroughCoreGate()
    {
        var service = new RustHybridCorrectionService(new FixedProvider(
            new RustShadowCandidateResult(
                RustShadowProviderState.Available,
                RustShadowDecision.Replace,
                RustShadowReason.Spelling,
                0.99,
                0.30,
                "привет")));

        var result = service.TryCreateApprovedDecision(
            "превет",
            JointCorrectionDecisionResult.Wait("превет"),
            LiveCorrectionTestHelpers.CreateStarterDictionary(),
            layoutEnabled: false,
            autocorrectEnabled: true,
            new AutocorrectionOptions(),
            new SentenceLanguageHint(TypingLanguage.Russian, 2, 0, 2, 12));

        Assert.NotNull(result);
        Assert.Equal(JointCorrectionRecommendation.Apply, result!.Recommendation);
        Assert.Equal(CorrectionKind.Autocorrect, result.Kind);
        Assert.Equal("привет", result.ReplacementToken);
    }

    [Fact]
    public void ExactKnownSource_CannotBeOverriddenByRust()
    {
        var service = new RustHybridCorrectionService(new FixedProvider(
            new RustShadowCandidateResult(
                RustShadowProviderState.Available,
                RustShadowDecision.Replace,
                RustShadowReason.Spelling,
                1,
                1,
                "сеня")));

        var result = service.TryCreateApprovedDecision(
            "меня",
            JointCorrectionDecisionResult.NoChange("меня"),
            LiveCorrectionTestHelpers.CreateStarterDictionary(),
            layoutEnabled: false,
            autocorrectEnabled: true,
            new AutocorrectionOptions(),
            SentenceLanguageHint.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void RustLayoutCandidate_UsesSameReplacementAndLanguageSwitchPath()
    {
        var service = new RustHybridCorrectionService(new FixedProvider(
            new RustShadowCandidateResult(
                RustShadowProviderState.Available,
                RustShadowDecision.Replace,
                RustShadowReason.Layout,
                1,
                1,
                "привет")));

        var result = service.TryCreateApprovedDecision(
            "ghbdtn",
            JointCorrectionDecisionResult.Wait("ghbdtn"),
            LiveCorrectionTestHelpers.CreateStarterDictionary(),
            layoutEnabled: true,
            autocorrectEnabled: false,
            new AutocorrectionOptions(),
            SentenceLanguageHint.Empty);

        Assert.NotNull(result);
        Assert.Equal(CorrectionKind.Layout, result!.Kind);
        Assert.Equal(LayoutConversionDirection.EnglishToRussian, result.LayoutDirection);
        Assert.Equal(SmartInput.Platform.Abstractions.Input.KeyboardInputLanguage.Russian, result.TargetInputLanguage);
    }

    [Fact]
    public void ExistingCoreApply_IsNeverOverridden()
    {
        var service = new RustHybridCorrectionService(new FixedProvider(
            new RustShadowCandidateResult(
                RustShadowProviderState.Available,
                RustShadowDecision.Replace,
                RustShadowReason.Spelling,
                1,
                1,
                "другой")));

        var core = new JointCorrectionDecisionResult
        {
            OriginalToken = "превет",
            ReplacementToken = "привет",
            Recommendation = JointCorrectionRecommendation.Apply,
            Kind = CorrectionKind.Autocorrect,
            ConfidenceScore = 1,
        };

        var result = service.TryCreateApprovedDecision(
            "превет",
            core,
            LiveCorrectionTestHelpers.CreateStarterDictionary(),
            layoutEnabled: false,
            autocorrectEnabled: true,
            new AutocorrectionOptions(),
            SentenceLanguageHint.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void LowFrequencyRustSpellingTarget_RemainsShadowOnly()
    {
        var service = new RustHybridCorrectionService(new FixedProvider(
            new RustShadowCandidateResult(
                RustShadowProviderState.Available,
                RustShadowDecision.Replace,
                RustShadowReason.Spelling,
                1,
                1,
                "привет")));
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.Russian, "привет", 0.69);

        var result = service.TryCreateApprovedDecision(
            "превет",
            JointCorrectionDecisionResult.Wait("превет"),
            dictionary,
            layoutEnabled: false,
            autocorrectEnabled: true,
            new AutocorrectionOptions(),
            new SentenceLanguageHint(TypingLanguage.Russian, 2, 0, 2, 12));

        Assert.Null(result);
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
