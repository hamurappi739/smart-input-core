using SmartInput.Core.Configuration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public sealed class ExternalAutocorrectionIntegrationTests
{
    [Fact]
    public void ExternalProviderIsOptInAndCanCorrectClassicRussianTypo()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.Russian, "мир", 0.80);

        var evaluator = new TrackingEvaluator();
        var service = new AutocorrectionService(
            externalEvaluator: evaluator,
            externalProvider: new FakeProvider(new ExternalSpellCandidate("мир", 1, 950_000)));

        _ = service.Evaluate("мирр", TypingLanguage.Russian, dictionary);
        Assert.False(evaluator.WasCalled);

        var enabled = service.Evaluate(
            "мирр",
            TypingLanguage.Russian,
            dictionary,
            new AutocorrectionOptions { UseExternalProvider = true });

        Assert.True(evaluator.WasCalled);
        Assert.Equal(AutocorrectionRecommendation.Candidate, enabled.Recommendation);
        Assert.Equal("мир", enabled.CandidateToken);
    }

    [Fact]
    public void CompositeProviderWorksWithInstalledOrBundledDictionary()
    {
        var dictionary = new TestAutocorrectDictionary();
        var provider = new CompositeExternalSpellCorrectionProvider(
            new SymSpellSpellCorrectionProvider(),
            new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));
        var service = new AutocorrectionService(
            externalEvaluator: new ExternalAutocorrectionEvaluator(),
            externalProvider: provider);

        var result = service.Evaluate(
            "превет",
            TypingLanguage.Russian,
            dictionary,
            new AutocorrectionOptions { UseExternalProvider = true });

        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal("привет", result.CandidateToken);
    }

    private sealed class TrackingEvaluator : IExternalAutocorrectionEvaluator
    {
        public bool WasCalled { get; private set; }

        public AutocorrectionResult Evaluate(
            string token,
            TypingLanguage activeLanguage,
            SmartInput.Core.Dictionaries.IAutocorrectDictionary dictionary,
            IExternalSpellCorrectionProvider provider,
            AutocorrectionOptions? options = null)
        {
            WasCalled = true;
            return new ExternalAutocorrectionEvaluator().Evaluate(
                token, activeLanguage, dictionary, provider, options);
        }
    }

    private sealed class FakeProvider(params ExternalSpellCandidate[] candidates)
        : IExternalSpellCorrectionProvider
    {
        public IReadOnlyList<ExternalSpellCandidate> FindCandidates(
            string token, TypingLanguage language, int maxEditDistance = 1) => candidates;
    }
}
