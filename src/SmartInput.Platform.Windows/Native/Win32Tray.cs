using System.Runtime.InteropServices;

namespace SmartInput.Platform.Windows.Native;

internal static class Win32Tray
{
    internal const int NimAdd = 0x00000000;
    internal const int NimModify = 0x00000001;
    internal const int NimDelete = 0x00000002;

    internal const int NifMessage = 0x00000001;
    internal const int NifIcon = 0x00000002;
    internal const int NifTip = 0x00000004;
    internal const int NifInfo = 0x00000010;

    internal const uint NiifInfo = 0x00000001;

    internal const int WmDestroy = 0x0002;
    internal const int WmCommand = 0x0111;
    internal const int WmRButtonUp = 0x0205;
    internal const int WmQuit = 0x0012;

    internal const uint WmUser = 0x0400;

    internal const uint MfString = 0x0000;
    internal const uint MfSeparator = 0x00000800;
    internal const uint MfChecked = 0x00000008;
    internal const uint TpmBottomAlign = 0x0020;
    internal const uint TpmLeftAlign = 0x0000;
    internal const uint TpmReturnCmd = 0x0100;

    internal const int CwUseDefault = unchecked((int)0x80000000);
    internal const int IdIcon = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public int CbSize;
        public nint HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public nint HIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SzTip;
        public uint DwState;
        public uint DwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SzInfo;
        public uint UVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SzInfoTitle;
        public uint DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShellNotifyIcon(int dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassW(ref WindowClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool AppendMenu(nint hMenu, uint uFlags, nint uIdNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    internal static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenu(
        nint hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        nint hWnd,
        nint prcRect);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out NativeMessage message, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    internal static extern void PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint hIcon);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        public nint HWnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Style;
        public nint LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
    }
}
