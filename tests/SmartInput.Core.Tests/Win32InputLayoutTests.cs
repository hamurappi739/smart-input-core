using System.Runtime.InteropServices;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Core.Tests;

public sealed class Win32InputLayoutTests
{
    [Fact]
    public void Input_UsesTheNativeWindowsInputStructureSize()
    {
        var expectedSize = IntPtr.Size == 8 ? 40 : 28;

        Assert.Equal(expectedSize, Marshal.SizeOf<Win32Input.Input>());
    }
}
