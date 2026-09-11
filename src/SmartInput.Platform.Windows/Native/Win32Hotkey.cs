using System.Runtime.InteropServices;

namespace SmartInput.Platform.Windows.Native;

internal static class Win32Hotkey
{
    internal const uint WmHotkey = 0x0312;

    internal const uint ModAlt = 0x0001;

    internal const uint ModControl = 0x0002;

    internal const uint ModShift = 0x0004;

    internal const uint ModWin = 0x0008;

    internal const uint ModNoRepeat = 0x4000;

    internal const int ErrorHotkeyAlreadyRegistered = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMessage(out Win32Keyboard.NativeMessage message, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        out Win32Keyboard.NativeMessage message,
        nint hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax,
        uint removeMessage);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Win32Keyboard.NativeMessage message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref Win32Keyboard.NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    internal const uint WmQuit = 0x0012;

    internal const uint WmApp = 0x8000;

    internal const uint PmNoRemove = 0x0000;

    internal const uint WmRegisterHotkey = WmApp + 1;

    internal const uint WmUnregisterHotkey = WmApp + 2;
}
