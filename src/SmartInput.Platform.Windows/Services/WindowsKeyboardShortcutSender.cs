using System.Runtime.InteropServices;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

internal static class WindowsKeyboardShortcutSender
{
    internal const ushort VkControl = 0x11;
    internal const ushort VkC = 0x43;
    internal const ushort VkV = 0x56;

    internal static void SendChord(ushort modifierVirtualKey, ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(modifierVirtualKey, 0),
            CreateKeyboardInput(virtualKey, 0),
            CreateKeyboardInput(virtualKey, Win32Input.KeyeventfKeyUp),
            CreateKeyboardInput(modifierVirtualKey, Win32Input.KeyeventfKeyUp),
        };

        if (Win32Input.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Input.Input>()) == 0)
        {
            throw new InvalidOperationException("SendInput failed for keyboard shortcut.");
        }
    }

    private static Win32Input.Input CreateKeyboardInput(ushort virtualKey, uint flags)
    {
        return new Win32Input.Input
        {
            Type = Win32Input.InputKeyboard,
            Data = new Win32Input.InputUnion
            {
                Keyboard = new Win32Input.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = flags,
                    ExtraInfo = SmartInputInjectionMarkers.SmartInputExtraInfo,
                },
            },
        };
    }
}
