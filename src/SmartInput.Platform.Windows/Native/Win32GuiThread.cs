using System.Runtime.InteropServices;

namespace SmartInput.Platform.Windows.Native;

internal static class Win32GuiThread
{
    [DllImport("user32.dll")]
    internal static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo info);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public int CbSize;
        public int Flags;
        public nint HwndActive;
        public nint HwndFocus;
        public nint HwndCapture;
        public nint HwndMenuOwner;
        public nint HwndMoveSize;
        public nint HwndCaret;
        public Rect RcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
