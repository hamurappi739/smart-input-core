using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "FocusedCorrection")]
public class R1CredibleLongerParentSemanticsTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly AutocorrectionOptions _options = new();

    [Theory]
    [InlineData("йда", "да", "айда", TypingLanguage.Russian)]
    [InlineData("ssure", "sure", "assure", TypingLanguage.English)]
    [InlineData("ooks", "looks", "books", TypingLanguage.English)]
    public void IntendedLongerMissingCharacterParent_ForcesWait(
        string token,
        string shorterUnique,
        string longerParent,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Equal(shorterUnique, analysis.UniqueTarget);
        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, longerParent, StringComparison.OrdinalIgnoreCase)
                && source.Operation == EditOperationType.MissingCharacter);
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));

        var service = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon());
        Assert.NotEqual(
            AutocorrectionRecommendation.Candidate,
            service.Evaluate(token, language, _dictionary, _options).Recommendation);
    }

    [Fact]
    public void SuffixGenSubExtension_ForcesWait_ForActorStyle()
    {
        var analysis = MutationClassificationOracle.Analyze(
            "actorr",
            TypingLanguage.English,
            _dictionary,
            _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, "actors", StringComparison.OrdinalIgnoreCase)
                && source.Operation == EditOperationType.GeneralSubstitution);
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Fact]
    public void SameLengthNearCostPeer_ForcesWait_EvenWhenFrequencyDominates()
    {
        // ыверь → Unique дверь; аверь is same-length near-cost vowel/gen peer.
        var analysis = MutationClassificationOracle.Analyze(
            "ыверь",
            TypingLanguage.Russian,
            _dictionary,
            _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, "аверь", StringComparison.OrdinalIgnoreCase));
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Fact]
    public void LongerTranspositionParentOfTypedToken_ForcesWait()
    {
        // агаат → Unique агат (Repeated); агата is longer AdjacentTransposition parent.
        var analysis = MutationClassificationOracle.Analyze(
            "агаат",
            TypingLanguage.Russian,
            _dictionary,
            _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, "агата", StringComparison.OrdinalIgnoreCase)
                && source.Operation == EditOperationType.AdjacentTransposition);
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Fact]
    public void MissingCharacterParentOfTypedToken_ForcesWait_EvenWhenUniqueIsDifferentSubstitution()
    {
        // вилкин → Unique силкин (AdjKey); авилкин is MissingCharacter parent of typed.
        var analysis = MutationClassificationOracle.Analyze(
            "вилкин",
            TypingLanguage.Russian,
            _dictionary,
            _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, "авилкин", StringComparison.OrdinalIgnoreCase)
                && source.Operation == EditOperationType.MissingCharacter);
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Fact]
    public void UnrelatedLongerPrefixWord_DoesNotForceWait_WhenUniqueIsIntended()
    {
        // аалка → алка (intended). палка is a longer insertion collision via AdjacentKey,
        // not a MissingCharacter parent of the typed mutation — must not force Wait.
        var analysis = MutationClassificationOracle.Analyze(
            "аалка",
            TypingLanguage.Russian,
            _dictionary,
            _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || !string.Equals(analysis.UniqueTarget, "алка", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Assert.False(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
        var service = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon());
        var result = service.Evaluate("аалка", TypingLanguage.Russian, _dictionary, _options);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal("алка", result.CandidateToken);
    }

    [Fact]
    public void ParentOutsideCostBand_DoesNotForceWait()
    {
        var winner = new PossibleMutationSource(
            Word: "hello",
            EditCost: 0.5,
            Operation: EditOperationType.MissingCharacter,
            Frequency: 0.999,
            DictionaryTier: 0,
            UsedAdjacentKeys: false);
        var farParent = new PossibleMutationSource(
            Word: "hellos",
            EditCost: winner.EditCost + 2.0,
            Operation: EditOperationType.MissingCharacter,
            Frequency: 0.80,
            DictionaryTier: 0,
            UsedAdjacentKeys: false);
        Assert.False(OperationPrecisionGate.IsCredibleLongerParentShortening(winner, farParent));
    }

    [Theory]
    [InlineData("превет", "привет", TypingLanguage.Russian)]
    [InlineData("helo", "hello", TypingLanguage.English)]
    [InlineData("teh", "the", TypingLanguage.English)]
    [InlineData("adn", "and", TypingLanguage.English)]
    [InlineData("миняй", "меняй", TypingLanguage.Russian)]
    public void ClearlyDominatedOrUltra_StillApplies(
        string token,
        string expected,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        Assert.Equal(MutationOracleClass.UniquelyRecoverable, analysis.Class);
        Assert.Equal(expected, analysis.UniqueTarget);
        Assert.False(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));

        var service = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon());
        var result = service.Evaluate(token, language, _dictionary, _options);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal(expected, result.CandidateToken);
    }
}
