using SmartInput.Platform.Windows.Services;

namespace SmartInput.Core.Tests;

public sealed class WindowsSecureInputDetectorTests
{
    [Theory]
    [InlineData("Edit", 0x0020L, true)]
    [InlineData("WindowsForms10.EDIT.app.0.2.15", 0L, false)]
    [InlineData("PasswordBox", 0L, true)]
    [InlineData("CustomCredentialHost", 0L, true)]
    [InlineData("Chrome_WidgetWin_1", 0L, false)]
    public void IsPasswordControl_UsesOnlyExplicitPasswordSignals(
        string className,
        long windowStyle,
        bool expected)
    {
        Assert.Equal(expected,
            WindowsSecureInputDetector.IsPasswordControl(className, windowStyle));
    }

    [Fact]
    public void IsPasswordControl_DoesNotTreatGuiStateAsSecureInput()
    {
        Assert.False(WindowsSecureInputDetector.IsPasswordControl("Edit", 0x0040L));
    }
}
