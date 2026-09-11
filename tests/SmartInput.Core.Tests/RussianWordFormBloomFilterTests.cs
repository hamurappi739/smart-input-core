using SmartInput.Core.Dictionaries;

namespace SmartInput.Core.Tests;

public sealed class RussianWordFormBloomFilterTests
{
    [Theory]
    [InlineData("работает")]
    [InlineData("работали")]
    [InlineData("собака")]
    [InlineData("красивыми")]
    public void MightContain_BundledRussianWordForm_ReturnsTrue(string word)
    {
        Assert.True(RussianWordFormBloomFilter.MightContain(word));
    }

    [Fact]
    public void MightContain_NonRussianToken_ReturnsFalseWithoutLoadingAnInvalidLookup()
    {
        Assert.False(RussianWordFormBloomFilter.MightContain("hello"));
    }
}
