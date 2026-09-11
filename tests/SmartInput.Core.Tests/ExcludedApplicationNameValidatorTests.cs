using SmartInput.Core.Validation;

namespace SmartInput.Core.Tests;

public class ExcludedApplicationNameValidatorTests
{
    [Fact]
    public void ValidateText_AcceptsSimpleProcessNames()
    {
        var result = ExcludedApplicationNameValidator.ValidateText("bankapp, customtool");

        Assert.True(result.IsValid);
        Assert.Equal(["bankapp", "customtool"], result.NormalizedEntries);
    }

    [Fact]
    public void ValidateText_StripsExeSuffix()
    {
        var result = ExcludedApplicationNameValidator.ValidateText("notepad.exe");

        Assert.True(result.IsValid);
        Assert.Equal(["notepad"], result.NormalizedEntries);
    }

    [Fact]
    public void ValidateText_RejectsPaths()
    {
        var result = ExcludedApplicationNameValidator.ValidateText(@"C:\Program Files\App\app.exe");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ValidateText_RejectsSpacesWithinName()
    {
        var result = ExcludedApplicationNameValidator.ValidateText("my app");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateText_DeduplicatesCaseInsensitive()
    {
        var result = ExcludedApplicationNameValidator.ValidateText("BankApp, bankapp");

        Assert.True(result.IsValid);
        Assert.Single(result.NormalizedEntries);
        Assert.Equal("BankApp", result.NormalizedEntries[0]);
    }

    [Fact]
    public void ParseEntries_SupportsNewLinesAndSemicolons()
    {
        var entries = ExcludedApplicationNameValidator.ParseEntries("a;b\nc");

        Assert.Equal(["a", "b", "c"], entries);
    }
}
