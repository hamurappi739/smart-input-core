using SmartInput.Platform.Windows.Native;
using SmartInput.Platform.Windows.Services;

namespace SmartInput.Core.Tests;

public sealed class WindowsSelectedTextServiceTests
{
    [Fact]
    public void BuildSelectedReplacementInputs_DeletesSelectionThenInsertsContiguousUnicodeText()
    {
        var inputs = WindowsSelectedTextService.BuildSelectedReplacementInputs("OK");

        Assert.Equal(6, inputs.Length);
        Assert.Equal(Win32Input.VkBack, inputs[0].Data.Keyboard.VirtualKey);
        Assert.Equal(0u, inputs[0].Data.Keyboard.Flags);
        Assert.Equal(Win32Input.KeyeventfKeyUp, inputs[1].Data.Keyboard.Flags);
        Assert.Equal((ushort)'O', inputs[2].Data.Keyboard.ScanCode);
        Assert.Equal(Win32Input.KeyeventfUnicode, inputs[2].Data.Keyboard.Flags);
        Assert.Equal((ushort)'K', inputs[4].Data.Keyboard.ScanCode);
        Assert.Equal(Win32Input.KeyeventfUnicode, inputs[4].Data.Keyboard.Flags);
    }

    [Fact]
    public void BuildSelectedReplacementInputs_EmptyReplacementStillDeletesSelection()
    {
        var inputs = WindowsSelectedTextService.BuildSelectedReplacementInputs(string.Empty);

        Assert.Equal(2, inputs.Length);
        Assert.Equal(Win32Input.VkBack, inputs[0].Data.Keyboard.VirtualKey);
        Assert.Equal(Win32Input.KeyeventfKeyUp, inputs[1].Data.Keyboard.Flags);
    }
}
