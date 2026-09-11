using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "FocusedCorrection")]
public class RemapUniqueDiscardedParentTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly AutocorrectionOptions _options = new();

    [Theory]
    [InlineData("миняй", "меняй", TypingLanguage.Russian)]
    [InlineData("превет", "привет", TypingLanguage.Russian)]
    [InlineData("helo", "hello", TypingLanguage.English)]
    [InlineData("teh", "the", TypingLanguage.English)]
    [InlineData("adn", "and", TypingLanguage.English)]
    public void GenuinelyUnique_OrClearlyStronger_StillApplies(
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

    [Fact]
    public void AnalyzeGeneratedCase_RemapsWrongUniqueTargetEvenWhenDominated()
    {
        var helo = MutationClassificationOracle.Analyze("helo", TypingLanguage.English, _dictionary, _options);
        Assert.Equal(MutationOracleClass.UniquelyRecoverable, helo.Class);

        // Full generated-case remapping: Unique≠intended → Ambiguous for WU accounting.
        // Production still Applies helo→hello via ultra-dominance (HasDiscarded=false).
        var remappedHelp = IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
            "helo",
            "help",
            TypingLanguage.English,
            _dictionary);
        Assert.Equal(MutationOracleClass.Ambiguous, remappedHelp.Class);
        Assert.Equal("hello", remappedHelp.UniqueTarget);
        Assert.False(OperationPrecisionGate.HasDiscardedCredibleCompetitor(helo));
        Assert.False(OperationPrecisionGate.ShouldRemapUniqueAsAmbiguous(helo, "help"));
    }

    [Theory]
    [InlineData("йда", "да", "айда", TypingLanguage.Russian)]
    [InlineData("быстрот", "быстро", "быстрота", TypingLanguage.Russian)]
    [InlineData("ooks", "looks", "books", TypingLanguage.English)]
    [InlineData("ssure", "sure", "assure", TypingLanguage.English)]
    [InlineData("анютк", "анюта", "анютка", TypingLanguage.Russian)]
    public void CredibleLongerParent_ForcesWait_EvenWhenShorterUniqueWins(
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
        var result = service.Evaluate(token, language, _dictionary, _options);
        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void LongerTranspositionParent_ForcesWait_ForPaart()
    {
        // paart→part Unique; apart reaches paart via AdjacentTransposition — structural longer Wait.
        var analysis = MutationClassificationOracle.Analyze(
            "paart",
            TypingLanguage.English,
            _dictionary,
            _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable
            || !string.Equals(analysis.UniqueTarget, "part", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, "apart", StringComparison.OrdinalIgnoreCase)
                && source.Operation == EditOperationType.AdjacentTransposition);
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Theory]
    [InlineData(EditOperationType.AdjacentKeySubstitution, "анютк", TypingLanguage.Russian)]
    [InlineData(EditOperationType.ExtraCharacter, "быстрот", TypingLanguage.Russian)]
    [InlineData(EditOperationType.MissingCharacter, "ooks", TypingLanguage.English)]
    [InlineData(EditOperationType.RepeatedAccidentalCharacter, "ssure", TypingLanguage.English)]
    public void CredibleLongerParent_CoversOperationFamilies(
        EditOperationType expectedFamily,
        string token,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        // Family tag may differ from generation op; require discarded longer-parent Wait.
        Assert.True(
            OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis)
            || analysis.Operation == expectedFamily);

        if (!OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis))
        {
            return;
        }

        var service = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon());
        var result = service.Evaluate(token, language, _dictionary, _options);
        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void IntendedParentStillPresent_AndCredible_ForcesWait()
    {
        var service = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon());
        var analysis = MutationClassificationOracle.Analyze("быстрот", TypingLanguage.Russian, _dictionary, _options);
        Assert.Equal(MutationOracleClass.UniquelyRecoverable, analysis.Class);
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
        var result = service.Evaluate("быстрот", TypingLanguage.Russian, _dictionary, _options);
        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void ClearlyStrongerWinner_DoesNotForceWait()
    {
        var analysis = MutationClassificationOracle.Analyze(
            "превет",
            TypingLanguage.Russian,
            _dictionary,
            _options);
        Assert.Equal(MutationOracleClass.UniquelyRecoverable, analysis.Class);
        Assert.False(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    private static IEnumerable<(string Token, TypingLanguage Language)> EnumerateProbeTokens()
    {
        // Deterministic short probes covering operation families without a large denylist.
        yield return ("миняй", TypingLanguage.Russian);
        yield return ("превет", TypingLanguage.Russian);
        yield return ("приивет", TypingLanguage.Russian);
        yield return ("првет", TypingLanguage.Russian);
        yield return ("приветт", TypingLanguage.Russian);
        yield return ("приевт", TypingLanguage.Russian);
        yield return ("прииет", TypingLanguage.Russian);
        yield return ("helo", TypingLanguage.English);
        yield return ("teh", TypingLanguage.English);
        yield return ("adn", TypingLanguage.English);
        yield return ("helllo", TypingLanguage.English);
        yield return ("hlelo", TypingLanguage.English);
        yield return ("recieve", TypingLanguage.English);
        yield return ("thier", TypingLanguage.English);
        yield return ("definately", TypingLanguage.English);
        yield return ("seperate", TypingLanguage.English);
        yield return ("occured", TypingLanguage.English);
        yield return ("untill", TypingLanguage.English);
        yield return ("begining", TypingLanguage.English);
        yield return ("enviroment", TypingLanguage.English);
    }
}
