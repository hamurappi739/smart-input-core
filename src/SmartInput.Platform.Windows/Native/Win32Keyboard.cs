using System.Runtime.InteropServices;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Platform.Windows.Native;

internal static class Win32Keyboard
{
    internal const uint LlkhfInjected = KeyboardHookFlags.Injected;

    internal const uint LlkhfExtended = 0x01;

    internal const int WhKeyboardLl = 13;

    internal const int WmKeyDown = 0x0100;

    internal const int WmKeyUp = 0x0101;

    internal const int WmSysKeyDown = 0x0104;

    internal const int WmSysKeyUp = 0x0105;

    internal const uint WmInputLangChangeRequest = 0x0050;

    internal delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct
    {
        public uint VkCode;

        public uint ScanCode;

        public uint Flags;

        public uint Time;

        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out NativeMessage message, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    internal static extern void PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    internal static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[]? lpList);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ToUnicodeEx(
        uint virtualKey,
        uint scanCode,
        byte[] keyboardState,
        [Out] char[] buffer,
        int bufferSize,
        uint flags,
        nint keyboardLayout);

    internal const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        public nint Hwnd;

        public uint Message;

        public nuint WParam;

        public nint LParam;

        public uint Time;

        public int PtX;

        public int PtY;
    }
}
