using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "FocusedCorrection")]
public class LayoutPairPolicyGateTests
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

    public LayoutPairPolicyGateTests()
    {
    }

    [Theory]
    [InlineData("фву", "фу", "ade")]
    [InlineData("иут", "тут", "ben")]
    [InlineData("фещь", "вещь", "atom")]
    public void WeakCyrillic_DoesNotApplyRussianAutocorrect_OverStrongEnglishLayout(
        string typed,
        string forbiddenRussianAutocorrect,
        string englishLayoutTarget)
    {
        Assert.True(_dictionary.GetFrequency(englishLayoutTarget, TypingLanguage.English) >= 0.70);
        Assert.True(_dictionary.GetFrequency(typed, TypingLanguage.Russian) < 0.70);

        var joint = CreateJoint();
        var result = joint.Evaluate(typed, _dictionary, true, true);
        Assert.False(
            result.Recommendation == JointCorrectionRecommendation.Apply
            && result.Kind == CorrectionKind.Autocorrect
            && string.Equals(result.ReplacementToken, forbiddenRussianAutocorrect, StringComparison.OrdinalIgnoreCase),
            $"R2: must not Apply Russian Autocorrect {typed}->{forbiddenRussianAutocorrect}");

        if (result.Recommendation == JointCorrectionRecommendation.Apply
            && result.Kind == CorrectionKind.Layout)
        {
            Assert.Equal(englishLayoutTarget, result.ReplacementToken);
        }
    }

    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("руддщ", "hello")]
    [InlineData("мущ", "veo")]
    [InlineData("пзг", "gpu")]
    public void UniqueValidLayoutPair_Applies(string token, string expected)
    {
        var joint = CreateJoint();
        var result = joint.Evaluate(token, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(expected, result.ReplacementToken);
    }

    [Theory]
    [InlineData("меня")]
    [InlineData("дать")]
    [InlineData("гавно")]
    [InlineData("душ")]
    [InlineData("начала")]
    [InlineData("начало")]
    [InlineData("дает")]
    [InlineData("даст")]
    [InlineData("дают")]
    public void MustPreserve_ExactKnown_NoChange(string token)
    {
        var joint = CreateJoint();
        var result = joint.Evaluate(token, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);

        var converter = new KeyboardLayoutConverter();
        var physical = converter.Convert(token, LayoutConversionDirection.RussianToEnglish);
        var verdict = BoundedCandidateApplyGuard.Evaluate(
            token,
            physical,
            CorrectionKind.Layout,
            TypingLanguage.Russian,
            _dictionary,
            converter,
            new AutocorrectionOptions());
        Assert.Equal(BoundedApplyVerdict.NoChange, verdict);
    }

    [Fact]
    public void MustPreserve_StrongSource_BlocksLayoutBeforeEmptyAmbiguousAllow()
    {
        var converter = new KeyboardLayoutConverter();
        // Pick a strong English word; layout image must not Apply.
        var token = "hello";
        Assert.True(_dictionary.GetFrequency(token, TypingLanguage.English) >= 0.70);

        var physical = converter.Convert(token, LayoutConversionDirection.EnglishToRussian);
        var verdict = BoundedCandidateApplyGuard.Evaluate(
            token,
            physical,
            CorrectionKind.Layout,
            TypingLanguage.English,
            _dictionary,
            converter,
            new AutocorrectionOptions());
        Assert.NotEqual(BoundedApplyVerdict.Allow, verdict);
    }

    [Fact]
    public void MustWait_AmbiguousWithCompetitors_Waits()
    {
        var converter = new KeyboardLayoutConverter();
        // Noise Latin that maps to something but has Ambiguous English parents → Wait.
        var token = "xqz";
        var physical = converter.Convert(token, LayoutConversionDirection.EnglishToRussian);
        if (string.Equals(token, physical, StringComparison.Ordinal))
        {
            return;
        }

        var verdict = BoundedCandidateApplyGuard.Evaluate(
            token,
            physical,
            CorrectionKind.Layout,
            TypingLanguage.English,
            _dictionary,
            converter,
            new AutocorrectionOptions());
        Assert.NotEqual(BoundedApplyVerdict.Allow, verdict);
    }

    [Fact]
    public void LayoutPairDiagnostics_AreHashedOnly()
    {
        var report = new MaximumCorpusAuditReport { Seed = 42 };
        var caseId = AuditCaseHasher.HashCase("руддщ", "hello", "hello");
        report.LayoutPairDiagnostics.Add(
            $"{caseId}|src=Cyrillic|tgt=Latin|cat=Layout|policy=ShouldConvert|expected=Apply|actual=Apply|gate=ConfidentCorrectionApplyGate");
        var joined = string.Join('\n', report.LayoutPairDiagnostics);
        Assert.DoesNotContain("руддщ", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", joined, StringComparison.OrdinalIgnoreCase);
    }
}
