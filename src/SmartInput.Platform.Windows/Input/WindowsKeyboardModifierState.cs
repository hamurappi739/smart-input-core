using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Input;

public sealed class WindowsKeyboardModifierState : IKeyboardModifierState
{
    public bool IsShiftDown => IsKeyDown(VirtualKeys.Shift)
        || IsKeyDown(VirtualKeys.LShift)
        || IsKeyDown(VirtualKeys.RShift);

    private static bool IsKeyDown(int virtualKey)
    {
        return (Win32Keyboard.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }
}
