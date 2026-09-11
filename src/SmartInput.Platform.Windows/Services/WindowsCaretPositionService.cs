using System.Runtime.InteropServices;
using SmartInput.Platform.Abstractions.Overlay;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsCaretPositionService : ICaretPositionService
{
    public Task<CaretScreenPosition?> GetCaretScreenPositionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TryGetCaretScreenPosition());
    }

    internal static CaretScreenPosition? TryGetCaretScreenPosition()
    {
        var foregroundWindow = Win32Window.GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return null;
        }

        var threadId = Win32Window.GetWindowThreadProcessId(foregroundWindow, out _);
        if (threadId == 0)
        {
            return null;
        }

        var info = new Win32GuiThread.GuiThreadInfo
        {
            CbSize = Marshal.SizeOf<Win32GuiThread.GuiThreadInfo>(),
        };

        if (!Win32GuiThread.GetGUIThreadInfo(threadId, ref info))
        {
            return null;
        }

        if (info.HwndCaret == 0)
        {
            return null;
        }

        var caretRect = info.RcCaret;
        var caretWidth = caretRect.Right - caretRect.Left;
        var caretHeight = caretRect.Bottom - caretRect.Top;

        if (caretWidth <= 0)
        {
            caretWidth = 2;
        }

        if (caretHeight <= 0)
        {
            caretHeight = 16;
        }

        var topLeft = new Win32Overlay.Point
        {
            X = caretRect.Left,
            Y = caretRect.Top,
        };

        if (!Win32Overlay.ClientToScreen(info.HwndCaret, ref topLeft))
        {
            return null;
        }

        return new CaretScreenPosition(
            topLeft.X + caretWidth,
            topLeft.Y,
            caretWidth,
            caretHeight,
            foregroundWindow);
    }
}
