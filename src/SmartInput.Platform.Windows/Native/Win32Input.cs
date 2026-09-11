using System.Runtime.InteropServices;

namespace SmartInput.Platform.Windows.Native;

internal static class Win32Input
{
    internal const uint InputKeyboard = 1;

    internal const uint KeyeventfKeyUp = 0x0002;

    internal const uint KeyeventfExtendedKey = 0x0001;

    internal const uint KeyeventfUnicode = 0x0004;

    internal const ushort VkBack = 0x08;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;

        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        // INPUT is sized by its largest native union member.  On 64-bit
        // Windows MOUSEINPUT is 32 bytes while KEYBDINPUT is 24 bytes.  The
        // mouse member is therefore required even though SmartInput only
        // sends keyboard input; without it Marshal.SizeOf<INPUT>() is 32
        // instead of the native 40 and SendInput rejects every call.
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int DeltaX;

        public int DeltaY;

        public uint MouseData;

        public uint Flags;

        public uint Time;

        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;

        public ushort ScanCode;

        public uint Flags;

        public uint Time;

        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);
}
