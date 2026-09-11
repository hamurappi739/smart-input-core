using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class MaximumAuditReproduceFailuresTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly JointCorrectionDecisionService _joint;

    public MaximumAuditReproduceFailuresTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(),
            converter);
    }

    [Theory]
    [InlineData("начала")]
    [InlineData("начало")]
    [InlineData("дает")]
    [InlineData("даёт")]
    [InlineData("даст")]
    [InlineData("дают")]
    [InlineData("дать")]
    [InlineData("все")]
    [InlineData("всё")]
    [InlineData("еще")]
    [InlineData("ещё")]
    public void ExactForms_MustRemainUnchanged(string word)
    {
        var result = _joint.Evaluate(word, _dictionary, true, true);
        Assert.True(
            result.Recommendation != JointCorrectionRecommendation.Apply,
            $"{word} changed to {result.ReplacementToken}/{result.Kind}; "
            + $"contains={_dictionary.Contains(word, TypingLanguage.Russian)} "
            + $"freq={_dictionary.GetFrequency(word, TypingLanguage.Russian):F3} "
            + $"trusted={TrustedWordAnalyzer.IsTrustedOriginal(word, _dictionary)} "
            + $"bloom={RussianWordFormBloomFilter.MightContain(word)}");
    }

    [Fact]
    public void Minyay_MustBecomeMenyay()
    {
        var result = _joint.Evaluate("миняй", _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal("меняй", result.ReplacementToken);
    }

    [Fact]
    public void Vbyzq_MustBecomeMenyay()
    {
        var result = _joint.Evaluate("vbyzq", _dictionary, true, true);
        Assert.Equal("меняй", result.ReplacementToken);
    }
}
