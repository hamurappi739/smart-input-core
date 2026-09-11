using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public sealed class RuleBasedPunctuationProviderTests
{
    private readonly RuleBasedPunctuationProvider _provider = new();

    [Theory]
    [InlineData("как дела", '?')]
    [InlineData("where are you", '?')]
    [InlineData("это обычная фраза", '.')]
    [InlineData("this is a sentence", '.')]
    public void AddsConservativeSentenceEnding(string text, char expectedMark)
    {
        var result = _provider.Analyze(text);

        var proposal = Assert.Single(result.Where(static item => item.AfterTokenIndex >= 0));
        Assert.Equal(expectedMark, proposal.Mark);
        Assert.True(proposal.Probability >= 0.90);
    }

    [Theory]
    [InlineData("я пришел но никого не увидел", 1)]
    [InlineData("я думаю что ты придешь", 1)]
    [InlineData("I stayed because it rained", 1)]
    public void AddsCommaOnlyAtNarrowConjunctionBoundary(string text, int afterToken)
    {
        var result = _provider.Analyze(text);

        Assert.Contains(result, proposal => proposal.AfterTokenIndex == afterToken && proposal.Mark == ',');
    }

    [Fact]
    public void DoesNotAddCommaAfterSingleLetterConjunction()
    {
        var result = _provider.Analyze("я и ты");

        Assert.DoesNotContain(result, proposal => proposal.Mark == ',');
    }

    [Fact]
    public void IsDeterministicAndDoesNotReturnText()
    {
        var first = _provider.Analyze("когда ты придешь");
        var second = _provider.Analyze("когда ты придешь");

        Assert.Equal(first, second);
        Assert.DoesNotContain(first, proposal => proposal.ProviderVersion.Contains(" ", StringComparison.Ordinal));
    }
}
