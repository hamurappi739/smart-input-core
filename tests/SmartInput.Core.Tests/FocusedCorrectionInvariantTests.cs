using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "FocusedCorrection")]
public class FocusedCorrectionInvariantTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    [Fact]
    public void ExtraCharacterCost_MatchesMissingCharacter_NotRepeatedAccidental()
    {
        Assert.Equal(ProductionEditCostTable.MissingCharacter, ProductionEditCostTable.ExtraCharacter);
        Assert.True(ProductionEditCostTable.ExtraCharacter > ProductionEditCostTable.AdjacentTransposition);
        Assert.True(ProductionEditCostTable.ExtraCharacter > ProductionEditCostTable.RepeatedAccidentalCharacter);
    }

    [Fact]
    public void ProductionAndVerifierEditCostTables_AreEquivalent()
    {
        foreach (EditOperationType operation in Enum.GetValues<EditOperationType>())
        {
            Assert.Equal(
                ProductionEditCostTable.ForOperation(operation),
                VerifierEditCostTable.ForOperation(operation));
        }
    }

    [Fact]
    public void CandidateGeneration_IsOrderIndependent_AndDeterministic()
    {
        var options = new AutocorrectionOptions { MaxGeneratedCandidates = 128 };
        var first = AutocorrectionCandidateGenerator.Generate("миняй", TypingLanguage.Russian, options);
        var second = AutocorrectionCandidateGenerator.Generate("миняй", TypingLanguage.Russian, options);
        Assert.Equal(first.Select(c => c.Word), second.Select(c => c.Word));
        Assert.Equal(first.Select(c => c.EditCost), second.Select(c => c.EditCost));
        Assert.True(first.Zip(first.Skip(1)).All(pair =>
            pair.First.EditCost < pair.Second.EditCost
            || (Math.Abs(pair.First.EditCost - pair.Second.EditCost) < 0.001
                && string.CompareOrdinal(pair.First.Word, pair.Second.Word) <= 0)));
    }

    [Fact]
    public void ExactKnownRussian_CannotBypassViaShortTechLatin()
    {
        var converter = new KeyboardLayoutConverter();
        foreach (var word in new[] { "гавно", "говно", "душ", "меня", "дать", "начала" })
        {
            var verdict = BoundedCandidateApplyGuard.Evaluate(
                word,
                "veo",
                CorrectionKind.Layout,
                TypingLanguage.Russian,
                _dictionary,
                converter,
                new AutocorrectionOptions());
            Assert.NotEqual(BoundedApplyVerdict.Allow, verdict);
        }
    }

    [Fact]
    public void UnknownNameAnchors_StillApply()
    {
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon(converter)),
            converter);

        var mush = joint.Evaluate("мущ", _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, mush.Recommendation);
        Assert.Equal("veo", mush.ReplacementToken);

        var gpu = joint.Evaluate("пзг", _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, gpu.Recommendation);
        Assert.Equal("gpu", gpu.ReplacementToken);
    }

    [Fact]
    public void DiagnosticArtifact_DoesNotContainRawLexiconWords()
    {
        var report = new MaximumCorpusAuditReport { Seed = 42 };
        MaximumCorpusAuditHarness.AddDiagnostic(
            report,
            MutationAmbiguityCluster.RepeatedCharacterAmbiguity,
            "миняй",
            "меняй",
            "меняй");

        var joined = string.Join('\n', report.DiagnosticClusterSamples);
        Assert.DoesNotContain("миняй", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("меняй", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", joined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("привет", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void IndependentVerifier_DoesNotCallProductionClassifyFromSources_ByContract()
    {
        // Differential: shared universe + independent classifier must agree with BF path.
        // Documented limitation: apply-band numeric constants match production by specification;
        // verifier still must not call production Apply gates.
        var brute = BruteForceMutationVerifier.Analyze("деелай", TypingLanguage.Russian, _dictionary);
        var independent = IndependentCorpusVerificationOracle.Analyze("деелай", TypingLanguage.Russian, _dictionary);
        Assert.Equal(brute.Class, independent.Class);
        Assert.Equal(brute.UniqueTarget, independent.UniqueTarget);

        var production = MutationClassificationOracle.Analyze("деелай", TypingLanguage.Russian, _dictionary);
        Assert.Equal(independent.Class, production.Class);
        Assert.Equal(independent.UniqueTarget, production.UniqueTarget);
    }
}
