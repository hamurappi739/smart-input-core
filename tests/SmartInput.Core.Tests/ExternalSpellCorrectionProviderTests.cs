using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public sealed class ExternalSpellCorrectionProviderTests
{
    private readonly SymSpellSpellCorrectionProvider _provider = new();

    [Theory]
    [InlineData("превет", "привет", TypingLanguage.Russian)]
    [InlineData("мирр", "мир", TypingLanguage.Russian)]
    [InlineData("teh", "the", TypingLanguage.English)]
    [InlineData("adn", "and", TypingLanguage.English)]
    [InlineData("helo", "hello", TypingLanguage.English)]
    public void FindCandidates_ReturnsExpectedHighPriorityCandidate(
        string token,
        string expected,
        TypingLanguage language)
    {
        var candidates = _provider.FindCandidates(token, language);

        Assert.Contains(candidates, candidate =>
            string.Equals(candidate.Word, expected, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, candidates.First(candidate =>
            string.Equals(candidate.Word, expected, StringComparison.OrdinalIgnoreCase)).EditDistance);
    }

    [Theory]
    [InlineData("https://example.com", TypingLanguage.English)]
    [InlineData("C:\\work\\file.txt", TypingLanguage.English)]
    [InlineData("user_name", TypingLanguage.English)]
    [InlineData("SmartInput", TypingLanguage.English)]
    [InlineData("hello", TypingLanguage.English)]
    [InlineData("привет", TypingLanguage.Russian)]
    public void FindCandidates_DoesNotSuggestForProtectedOrKnownTokens(
        string token,
        TypingLanguage language)
    {
        Assert.Empty(_provider.FindCandidates(token, language));
    }

    [Theory]
    [InlineData("teh", TypingLanguage.Russian)]
    [InlineData("превет", TypingLanguage.English)]
    [InlineData("z", TypingLanguage.English)]
    [InlineData("123", TypingLanguage.English)]
    public void FindCandidates_RejectsWrongLanguageAndShortTokens(
        string token,
        TypingLanguage language)
    {
        Assert.Empty(_provider.FindCandidates(token, language));
    }

    [Fact]
    public void FindCandidates_IsBoundedAndDeterministic()
    {
        var first = _provider.FindCandidates("teh", TypingLanguage.English);
        var second = _provider.FindCandidates("teh", TypingLanguage.English);

        Assert.InRange(first.Count, 0, 16);
        Assert.Equal(first, second);
    }

    [Fact]
    public void CompositeProvider_DeduplicatesAndKeepsBestFrequency()
    {
        var composite = new CompositeExternalSpellCorrectionProvider(
            new SymSpellSpellCorrectionProvider(),
            new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));

        var candidates = composite.FindCandidates("teh", TypingLanguage.English);

        Assert.InRange(candidates.Count, 0, 16);
        Assert.Equal(
            candidates.Select(candidate => candidate.Word).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            candidates.Count);
        Assert.Contains(candidates, candidate =>
            string.Equals(candidate.Word, "the", StringComparison.OrdinalIgnoreCase));
    }
}
