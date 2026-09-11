using SmartInput.Core.Engines;

namespace SmartInput.Core.Tests;

public class KeyboardLayoutConverterTests
{
    private readonly KeyboardLayoutConverter _converter = new();

    [Fact]
    public void Convert_EnglishToRussian_ConvertsGhbdtnToPrivet()
    {
        var result = _converter.Convert("ghbdtn", LayoutConversionDirection.EnglishToRussian);

        Assert.Equal("привет", result);
    }

    [Fact]
    public void Convert_RussianToEnglish_ConvertsRuddshToHello()
    {
        var result = _converter.Convert("руддщ", LayoutConversionDirection.RussianToEnglish);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void Convert_EnglishToRussian_ReverseOfRussianToEnglish()
    {
        var english = "ghbdtn";
        var russian = _converter.Convert(english, LayoutConversionDirection.EnglishToRussian);
        var roundTrip = _converter.Convert(russian, LayoutConversionDirection.RussianToEnglish);

        Assert.Equal(english, roundTrip);
    }

    [Fact]
    public void Convert_RussianToEnglish_ReverseOfEnglishToRussian()
    {
        var russian = "привет";
        var english = _converter.Convert(russian, LayoutConversionDirection.RussianToEnglish);
        var roundTrip = _converter.Convert(english, LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(russian, roundTrip);
    }

    [Fact]
    public void Convert_PreservesCase()
    {
        Assert.Equal("Привет", _converter.Convert("Ghbdtn", LayoutConversionDirection.EnglishToRussian));
        Assert.Equal("ПРИВЕТ", _converter.Convert("GHBDTN", LayoutConversionDirection.EnglishToRussian));
        Assert.Equal("Hello", _converter.Convert("Руддщ", LayoutConversionDirection.RussianToEnglish));
    }

    [Fact]
    public void Convert_PreservesDigitsSpacesAndUnknownCharacters()
    {
        var input = "123 © ghbdtn!";
        var converted = _converter.Convert(input, LayoutConversionDirection.EnglishToRussian);

        Assert.Equal("123 © привет!", converted);
    }

    [Fact]
    public void Convert_PreservesMappedPunctuation()
    {
        Assert.Equal("\"", _converter.Convert("@", LayoutConversionDirection.EnglishToRussian));
        Assert.Equal("№", _converter.Convert("#", LayoutConversionDirection.EnglishToRussian));
        Assert.Equal("@", _converter.Convert("\"", LayoutConversionDirection.RussianToEnglish));
    }

    [Fact]
    public void Convert_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _converter.Convert(string.Empty, LayoutConversionDirection.EnglishToRussian));
    }

    [Fact]
    public void Convert_RoundTrip_AllMappedCharacters()
    {
        var englishChars = "`qwertyuiop[]asdfghjkl;'\\zxcvbnm,./~@#$^&:" + "\"<>?";
        var russian = _converter.Convert(englishChars, LayoutConversionDirection.EnglishToRussian);
        var roundTrip = _converter.Convert(russian, LayoutConversionDirection.RussianToEnglish);

        Assert.Equal(englishChars, roundTrip);
    }
}
