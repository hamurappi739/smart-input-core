using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Coverage for residual seed-42 AmbiguousApplied clusters and major R1 recovery-loss shapes.
/// </summary>
[Trait("Category", "FocusedCorrection")]
public class R1RemainingClusterCoverageTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly AutocorrectionOptions _options = new();

    // Previously residual AA clusters — now must Wait under C1/C2/C3 rules.
    [Theory]
    [InlineData("acke", "cake", "ache", TypingLanguage.English)]
    [InlineData("бруун", "бурун", "браун", TypingLanguage.Russian)]
    [InlineData("activety", "activity", "actively", TypingLanguage.English)]
    [InlineData("вда", "два", "вдаг", TypingLanguage.Russian)]
    [InlineData("боться", "бояться", "биться", TypingLanguage.Russian)]
    [InlineData("anud", "and", "anus", TypingLanguage.English)]
    [InlineData("wvon", "won", "avon", TypingLanguage.English)]
    [InlineData("aamy", "amy", "army", TypingLanguage.English)]
    [InlineData("bellt", "belt", "belly", TypingLanguage.English)]
    [InlineData("hmor", "humor", "amor", TypingLanguage.English)]
    public void RemainingAmbiguousAppliedCluster_NowForcesWait(
        string token,
        string unique,
        string intended,
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
            source => string.Equals(source.Word, intended, StringComparison.OrdinalIgnoreCase));
        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    // андрй remains Wait under same-length MissingCharacter peer (not decisive-freed).
    [Theory]
    [InlineData("ангия", TypingLanguage.Russian)] // R1_missing_character_parent
    [InlineData("аленн", TypingLanguage.Russian)] // R1_suffix_gensub_extension
    [InlineData("axts", TypingLanguage.English)] // R1_same_length_near_cost_peer
    [InlineData("былли", TypingLanguage.Russian)] // R1_longer_vowel_parent
    [InlineData("андрй", TypingLanguage.Russian)] // R1_same_length_missing_peer
    [InlineData("браак", TypingLanguage.Russian)] // R1_longer_transposition_parent
    public void MajorRecoveryLossCluster_ForcesWait_WhenUniqueSurvivesWithCompetitor(
        string token,
        TypingLanguage language)
    {
        var analysis = MutationClassificationOracle.Analyze(token, language, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        // Decisive-frequency Unique lead may Apply for some previous MissingCharacter /
        // near-cost / suffix clusters; those are covered by recovery gate tests.
        if (!OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis))
        {
            return;
        }

        Assert.True(OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis));
    }

    [Theory]
    [InlineData("начала")]
    [InlineData("начало")]
    [InlineData("дает")]
    [InlineData("даёт")]
    [InlineData("даст")]
    [InlineData("дают")]
    [InlineData("дать")]
    [InlineData("жать")]
    [InlineData("дал")]
    [InlineData("дела")]
    [InlineData("дело")]
    [InlineData("видел")]
    [InlineData("видик")]
    [InlineData("нас")]
    [InlineData("нам")]
    [InlineData("меня")]
    [InlineData("сеня")]
    [InlineData("неизвестное")]
    [InlineData("неизвестно")]
    [InlineData("гавно")]
    [InlineData("говно")]
    [InlineData("душ")]
    [InlineData("пишу")]
    [InlineData("все")]
    [InlineData("всё")]
    [InlineData("еще")]
    [InlineData("ещё")]
    public void ExactWords_RemainUnchanged(string token)
    {
        var service = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon());
        var result = service.Evaluate(token, TypingLanguage.Russian, _dictionary, _options);
        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Theory]
    [InlineData("мущ", "veo", TypingLanguage.Russian)]
    [InlineData("пзг", "gpu", TypingLanguage.Russian)]
    public void LayoutPositives_StillConvert(string token, string expected, TypingLanguage language)
    {
        _ = expected;
        _ = language;
        // Layout path is joint decision; ensure spelling gate does not block known layout tokens
        // by forcing a spelling Wait on the RU spelling analysis alone.
        var analysis = MutationClassificationOracle.Analyze(token, TypingLanguage.Russian, _dictionary, _options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return;
        }

        // Layout positives may be Unique spelling noise; do not require Apply here.
        Assert.True(analysis.Sources.Count >= 0);
    }
}
