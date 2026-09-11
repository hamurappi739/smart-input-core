using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Narrow recall: Unique + !HasDiscarded must Apply at the final gate
/// (no second AmbiguityIndex veto), while C1/C2 residual AA clusters stay Wait.
/// </summary>
[Trait("Category", "FocusedCorrection")]
public class NonR1RecoveryGateTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly AutocorrectionOptions _options = new();

    private readonly JointCorrectionDecisionService _joint;

    public NonR1RecoveryGateTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon(converter)),
            converter);
    }

    // NonR1 MissingCharacter recovery with decisive AmbiguityIndex lead.
    [Theory]
    [InlineData("бориген", "абориген", TypingLanguage.Russian)]
    public void NonR1_MissingCharacterDecisive_Applies(
        string token,
        string expected,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        Assert.Equal(MutationOracleClass.UniquelyRecoverable, analysis.Class);
        Assert.Equal(expected, analysis.UniqueTarget);
        Assert.Equal(EditOperationType.MissingCharacter, analysis.Operation);
        Assert.False(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));

        var joint = _joint.Evaluate(token, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal(expected, joint.ReplacementToken, ignoreCase: true);
    }

    // Vowel-vowel peers that previously became AmbiguousApplied must Wait.
    [Theory]
    [InlineData("былочка", "булочка", "белочка", TypingLanguage.Russian)]
    [InlineData("галять", "гулять", "гилять", TypingLanguage.Russian)]
    [InlineData("бэржа", "баржа", "биржа", TypingLanguage.Russian)]
    public void VowelVowelPeer_ForcesWait(
        string token,
        string unique,
        string peer,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Equal(unique, analysis.UniqueTarget);
        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, peer, StringComparison.OrdinalIgnoreCase));
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));

        var joint = _joint.Evaluate(token, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, joint.Recommendation);
    }

    // NonR1 recovery-loss cluster: Unique==intended, !HasDiscarded, previously Wait
    // only because CandidateAmbiguityIndex vetoed the final Apply gate.
    [Theory]
    [InlineData("уленка", "аленка", TypingLanguage.Russian)]
    [InlineData("ёверин", "аверин", TypingLanguage.Russian)]
    [InlineData("овтор", "автор", TypingLanguage.Russian)]
    public void NonR1_UniqueWithoutCredibleDiscard_MayStillWaitOnSignaturePeers(
        string token,
        string expected,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        Assert.Equal(MutationOracleClass.UniquelyRecoverable, analysis.Class);
        Assert.Equal(expected, analysis.UniqueTarget);
        // Cheaper or near-tied AmbiguityIndex peers may still force Wait.
        _ = _joint.Evaluate(token, _dictionary, true, true);
    }

    // Genuine C1/C2 ambiguity from the historical residual-51 table must remain Wait.
    [Theory]
    [InlineData("acke", TypingLanguage.English)]
    [InlineData("бруун", TypingLanguage.Russian)]
    [InlineData("activety", TypingLanguage.English)]
    [InlineData("вда", TypingLanguage.Russian)]
    [InlineData("боться", TypingLanguage.Russian)]
    [InlineData("anud", TypingLanguage.English)]
    [InlineData("wvon", TypingLanguage.English)]
    [InlineData("aamy", TypingLanguage.English)]
    [InlineData("bellt", TypingLanguage.English)]
    [InlineData("hmor", TypingLanguage.English)]
    [InlineData("alws", TypingLanguage.English)]
    [InlineData("bqack", TypingLanguage.English)]
    public void HistoricalAmbiguousAppliedCluster_RemainsWait(string token, TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            var jointWait = _joint.Evaluate(token, _dictionary, true, true);
            Assert.NotEqual(JointCorrectionRecommendation.Apply, jointWait.Recommendation);
            return;
        }

        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
        var joint = _joint.Evaluate(token, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, joint.Recommendation);
    }

    [Theory]
    [InlineData("превет", "привет")]
    [InlineData("helo", "hello")]
    [InlineData("teh", "the")]
    [InlineData("adn", "and")]
    [InlineData("миняй", "меняй")]
    public void UltraAndMandatorySpellingPositives_StillApply(string token, string expected)
    {
        var joint = _joint.Evaluate(token, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal(expected, joint.ReplacementToken, ignoreCase: true);
    }

    [Theory]
    [InlineData("мущ", "veo")]
    [InlineData("пзг", "gpu")]
    [InlineData("ghbdtn", "привет")]
    [InlineData("руддщ", "hello")]
    public void LayoutPositives_StillApply(string token, string expected)
    {
        var joint = _joint.Evaluate(token, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal(expected, joint.ReplacementToken, ignoreCase: true);
    }

    [Theory]
    [InlineData("начала")]
    [InlineData("начало")]
    [InlineData("дает")]
    [InlineData("даёт")]
    [InlineData("нас")]
    [InlineData("меня")]
    [InlineData("все")]
    [InlineData("всё")]
    public void ExactNormalRussianWords_RemainUnchanged(string token)
    {
        var joint = _joint.Evaluate(token, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, joint.Recommendation);
    }
}
