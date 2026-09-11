using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "FocusedCorrection")]
public class Residual51ClusterRuleTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly AutocorrectionOptions _options = new();

    [Theory]
    [InlineData("acke", "cake", "ache", TypingLanguage.English)]
    [InlineData("бруун", "бурун", "браун", TypingLanguage.Russian)]
    [InlineData("alws", "laws", "alas", TypingLanguage.English)]
    public void Cluster1_TranspositionSameLengthPeer_ForcesWait(
        string token,
        string selected,
        string competitor,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Equal(selected, analysis.UniqueTarget);
        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, competitor, StringComparison.OrdinalIgnoreCase));
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Theory]
    [InlineData("teh", "the", TypingLanguage.English)]
    [InlineData("adn", "and", TypingLanguage.English)]
    [InlineData("recieve", "receive", TypingLanguage.English)]
    public void Cluster1_UltraTransposition_StillApplies(string token, string expected, TypingLanguage language)
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

    [Theory]
    [InlineData("wvon", "won", "avon", TypingLanguage.English)]
    [InlineData("aamy", "amy", "army", TypingLanguage.English)]
    [InlineData("bqack", "back", "black", TypingLanguage.English)]
    [InlineData("anud", "and", "anus", TypingLanguage.English)]
    public void Cluster2_NonPrefixLongerShortening_ForcesWait(
        string token,
        string selected,
        string competitor,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Equal(selected, analysis.UniqueTarget);
        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, competitor, StringComparison.OrdinalIgnoreCase));
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Fact]
    public void Cluster2_PrefixCollision_Aalka_DoesNotForceWait()
    {
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

    [Theory]
    [InlineData("боться", "бояться", "биться", TypingLanguage.Russian)]
    [InlineData("hmor", "humor", "amor", TypingLanguage.English)]
    public void Cluster3_ShorterTokenLengthPeer_ForcesWait(
        string token,
        string selected,
        string competitor,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Equal(selected, analysis.UniqueTarget);
        Assert.Contains(
            analysis.Sources,
            source => string.Equals(source.Word, competitor, StringComparison.OrdinalIgnoreCase));
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Theory]
    [InlineData("вда", "два", TypingLanguage.Russian)]
    [InlineData("elt", "let", TypingLanguage.English)]
    public void NearUltraTransposition_WithLongerMissingParent_ForcesWait(
        string token,
        string selected,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        Assert.Equal(selected, analysis.UniqueTarget);
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }
}
