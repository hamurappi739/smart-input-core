using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class PunctuationCorrectionServiceTests
{
    private readonly PunctuationCorrectionService _service = new();

    [Theory]
    [InlineData("Привет ", ',', " ")]
    [InlineData("Как дела ", '?', " ")]
    [InlineData("Это важно ", '!', " ")]
    [InlineData("Что это ", ':', " ")]
    [InlineData("Проверь ", ';', " ")]
    public void Evaluate_PositiveCases_RemovesSingleSpaceBeforePunctuation(
        string textBeforePunctuation,
        char punctuation,
        string expectedOriginalSegment)
    {
        var result = _service.Evaluate(textBeforePunctuation, punctuation);

        Assert.Equal(PunctuationCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(expectedOriginalSegment, result.OriginalSegment);
        Assert.Equal(string.Empty, result.ReplacementSegment);
        Assert.Equal(punctuation, result.PunctuationCharacter);
    }

    [Theory]
    [InlineData("3.14", '.')]
    [InlineData("v1.2.3", '.')]
    [InlineData("https://example.com", '.')]
    [InlineData("user@example.com", '.')]
    [InlineData(@"C:\Users\Test\file.txt", '.')]
    [InlineData("/usr/local/bin/tool", '/')]
    [InlineData("snake_case", '_')]
    [InlineData("camelCase", 'C')]
    [InlineData("т.д.", '.')]
    [InlineData("и т.п.", '.')]
    [InlineData("...", '.')]
    public void Evaluate_PreserveCases_ReturnsNoChange(string text, char punctuation)
    {
        var result = _service.Evaluate(text, punctuation);

        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_EmptyText_ReturnsNoChange()
    {
        var result = _service.Evaluate(string.Empty, ',');

        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_AlreadyCorrectSpacing_ReturnsNoChange()
    {
        var result = _service.Evaluate("Привет,", ',');

        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_MultipleIntentionalSpaces_DoesNotCollapseGlobally()
    {
        var result = _service.Evaluate("word  ,", ',');

        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_PreservesTabsAndNewlines()
    {
        var withTab = _service.Evaluate("line\t", ',');
        var withNewline = _service.Evaluate("line\n", ',');

        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, withTab.Recommendation);
        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, withNewline.Recommendation);
    }

    [Fact]
    public void Evaluate_UnicodeText_IsPreservedInContext()
    {
        var result = _service.Evaluate("Привет мир 🌍 ", '!');

        Assert.Equal(PunctuationCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(" ", result.OriginalSegment);
    }

    [Fact]
    public void Evaluate_PunctuationAtBeginningOrEnd_IsSafe()
    {
        Assert.Equal(
            PunctuationCorrectionRecommendation.NoChange,
            _service.Evaluate(",", ',').Recommendation);
        Assert.Equal(
            PunctuationCorrectionRecommendation.NoChange,
            _service.Evaluate(" ", ',').Recommendation);
    }

    [Fact]
    public void Evaluate_AbbreviationBeforeComma_PreservesSpace()
    {
        var result = _service.Evaluate("и т.п. ", ',');

        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, result.Recommendation);
    }

    [Fact]
    public void Evaluate_DecimalWithTrailingSpaceBeforeQuestion_RemovesSpace()
    {
        var result = _service.Evaluate("Value is 3.14 ", '?');

        Assert.Equal(PunctuationCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Fact]
    public void Evaluate_NonTargetPunctuationCharacter_ReturnsNoChange()
    {
        var result = _service.Evaluate("test ", '@');

        Assert.Equal(PunctuationCorrectionRecommendation.NoChange, result.Recommendation);
    }
}
