using SmartInput.Core.Engines;

namespace SmartInput.Core.Tests;

public class LayoutConversionProbeTests
{
    [Theory]
    [InlineData("мущ", "veo")]
    [InlineData("пзг", "gpu")]
    public void Convert_RussianToEnglish_MapsExpectedLatin(string russian, string expectedLatin)
    {
        var converter = new KeyboardLayoutConverter();
        Assert.Equal(expectedLatin, converter.Convert(russian, LayoutConversionDirection.RussianToEnglish));
    }
}
