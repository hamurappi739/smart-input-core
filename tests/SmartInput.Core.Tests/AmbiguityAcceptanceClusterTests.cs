using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "FocusedCorrection")]
public class AmbiguityAcceptanceClusterTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private JointCorrectionDecisionService CreateJoint()
    {
        var converter = new KeyboardLayoutConverter();
        return new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon(converter)),
            converter);
    }

    public AmbiguityAcceptanceClusterTests()
    {
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
    public void RequiredPreserves_DoNotApply(string token)
    {
        var joint = CreateJoint();
        var result = joint.Evaluate(token, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("руддщ", "hello")]
    [InlineData("мущ", "veo")]
    [InlineData("пзг", "gpu")]
    [InlineData("миняй", "меняй")]
    [InlineData("vbyzq", "меняй")]
    [InlineData("gtie", "пишу")]
    [InlineData("превет", "привет")]
    [InlineData("helo", "hello")]
    [InlineData("teh", "the")]
    [InlineData("adn", "and")]
    public void RequiredPositives_Apply(string token, string expected)
    {
        var joint = CreateJoint();
        var result = joint.Evaluate(token, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(expected, result.ReplacementToken);
    }

    [Fact]
    public void ExactKnownSource_BlocksUnknownNameShortcut()
    {
        var converter = new KeyboardLayoutConverter();
        foreach (var word in new[] { "меня", "дать", "гавно", "душ", "начала", "начало", "дает", "даст", "дают" })
        {
            var physical = converter.Convert(word, LayoutConversionDirection.RussianToEnglish);
            var verdict = BoundedCandidateApplyGuard.Evaluate(
                word,
                physical,
                CorrectionKind.Layout,
                TypingLanguage.Russian,
                _dictionary,
                converter,
                new AutocorrectionOptions());
            Assert.Equal(BoundedApplyVerdict.NoChange, verdict);
        }
    }

    [Fact]
    public void ShortLatinCompetingWithKnownRussianSpelling_DoesNotBypassExactKnown()
    {
        var converter = new KeyboardLayoutConverter();
        var verdict = BoundedCandidateApplyGuard.Evaluate(
            "душ",
            "veo",
            CorrectionKind.Layout,
            TypingLanguage.Russian,
            _dictionary,
            converter,
            new AutocorrectionOptions());
        Assert.NotEqual(BoundedApplyVerdict.Allow, verdict);
    }

    [Fact]
    public void CrossOperationCompetitors_RemainVisibleBeforeFiltering()
    {
        var analysis = MutationClassificationOracle.Analyze(
            "превет",
            TypingLanguage.Russian,
            _dictionary,
            new AutocorrectionOptions());
        Assert.Contains(
            analysis.Sources,
            source => source.Operation is EditOperationType.VowelSubstitution
                or EditOperationType.AdjacentKeySubstitution
                or EditOperationType.GeneralSubstitution
                or EditOperationType.MissingCharacter
                or EditOperationType.ExtraCharacter
                or EditOperationType.AdjacentTransposition
                or EditOperationType.RepeatedAccidentalCharacter);
    }

    [Fact]
    public void AnalyzeGeneratedCase_RemapsWrongUniqueTargetToAmbiguous_WhenCredible()
    {
        // Full generated-case remapping: Unique≠intended → Ambiguous.
        // Production Wait predicate remains ShouldRemap / HasDiscarded (near-cost).
        var mutation = "helo";
        var production = MutationClassificationOracle.Analyze(
            mutation,
            TypingLanguage.English,
            _dictionary,
            new AutocorrectionOptions());
        Assert.Equal(MutationOracleClass.UniquelyRecoverable, production.Class);

        var remappedHelp = IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
            mutation,
            "help",
            TypingLanguage.English,
            _dictionary);
        Assert.Equal(MutationOracleClass.Ambiguous, remappedHelp.Class);
        Assert.False(OperationPrecisionGate.ShouldRemapUniqueAsAmbiguous(production, "help"));

        // Synthetic: claim intended equals a credible alternate when discarded-competitor exists.
        foreach (var (token, language) in new (string, TypingLanguage)[]
                 {
                     ("helllo", TypingLanguage.English),
                     ("приивет", TypingLanguage.Russian),
                     ("untill", TypingLanguage.English),
                 })
        {
            var analysis = MutationClassificationOracle.Analyze(
                token,
                language,
                _dictionary,
                new AutocorrectionOptions());
            if (analysis.Class != MutationOracleClass.UniquelyRecoverable
                || !OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis))
            {
                continue;
            }

            var alternate = analysis.Sources.First(source =>
                !string.Equals(source.Word, analysis.UniqueTarget, StringComparison.OrdinalIgnoreCase));
            var remapped = IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
                token,
                alternate.Word,
                language,
                _dictionary);
            Assert.Equal(MutationOracleClass.Ambiguous, remapped.Class);
            return;
        }
    }

    [Fact]
    public void DiscardedCredibleCompetitor_ForcesSpellingWait_OnRemapShape()
    {
        var options = new AutocorrectionOptions();
        var service = new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon());

        // Synthetic remap shape: RepeatedAccidental Unique while a higher-frequency
        // parent of another operation remains in Sources — production must Wait.
        var analysis = MutationClassificationOracle.Analyze(
            "преветт",
            TypingLanguage.Russian,
            _dictionary,
            options);
        if (analysis.Class == MutationOracleClass.UniquelyRecoverable
            && OperationPrecisionGate.HasDiscardedCredibleCompetitor(analysis))
        {
            var result = service.Evaluate("преветт", TypingLanguage.Russian, _dictionary, options);
            Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
            return;
        }

        // Fallback: helo must remain Apply (ultra-freq insert/delete exception).
        var helo = service.Evaluate("helo", TypingLanguage.English, _dictionary, options);
        Assert.Equal(AutocorrectionRecommendation.Candidate, helo.Recommendation);
        Assert.Equal("hello", helo.CandidateToken);
    }

    [Fact]
    public void PrivacySafeDiagnostic_HashesDoNotLeakRawTokens()
    {
        foreach (var (token, source) in new[]
                 {
                     ("миняй", "меняй"),
                     ("helo", "hello"),
                     ("превет", "привет"),
                 })
        {
            var caseId = AuditCaseHasher.HashCase(token, source, null);
            Assert.DoesNotContain(token, caseId, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(source, caseId, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(16, caseId.Length);
        }
    }
}
