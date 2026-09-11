using System.Runtime.InteropServices;
using System.Text;
using SmartInput.Platform.Abstractions.Security;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsSecureInputDetector : ISecureInputDetector
{
    private const long EditPasswordStyle = 0x0020;
    private const int WindowClassNameCapacity = 256;

    public Task<SecureInputDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windowHandle = Win32Window.GetForegroundWindow();
        if (windowHandle == 0)
        {
            return Task.FromResult(new SecureInputDetectionResult(SecureInputState.Unknown));
        }

        var threadId = Win32Window.GetWindowThreadProcessId(windowHandle, out _);
        if (threadId == 0)
        {
            return Task.FromResult(new SecureInputDetectionResult(SecureInputState.Unknown));
        }

        var threadInfo = new Win32GuiThread.GuiThreadInfo
        {
            CbSize = Marshal.SizeOf<Win32GuiThread.GuiThreadInfo>(),
        };

        if (!Win32GuiThread.GetGUIThreadInfo(threadId, ref threadInfo))
        {
            return Task.FromResult(new SecureInputDetectionResult(SecureInputState.Unknown));
        }

        // GUITHREADINFO.Flags contains GUI state flags (caret/menu/move-size),
        // not a secure-input bit.  The old 0x40 check therefore never gave a
        // valid password-field signal.  Inspect the actual focused control
        // instead.  If Win32 cannot identify it, fail closed.
        var focusedWindow = threadInfo.HwndFocus != 0
            ? threadInfo.HwndFocus
            : threadInfo.HwndActive != 0
                ? threadInfo.HwndActive
                : windowHandle;

        var className = new StringBuilder(WindowClassNameCapacity);
        if (Win32Window.GetClassName(focusedWindow, className, className.Capacity) <= 0)
        {
            return Task.FromResult(new SecureInputDetectionResult(SecureInputState.Unknown));
        }

        Marshal.SetLastPInvokeError(0);
        var windowStyle = Win32Window.GetWindowLongPtr(focusedWindow, Win32Window.GwlStyle).ToInt64();
        if (Marshal.GetLastPInvokeError() != 0 && windowStyle == 0)
        {
            return Task.FromResult(new SecureInputDetectionResult(SecureInputState.Unknown));
        }

        var isSecure = IsPasswordControl(className.ToString(), windowStyle);
        return Task.FromResult(new SecureInputDetectionResult(
            isSecure ? SecureInputState.Active : SecureInputState.Inactive));
    }

    internal static bool IsPasswordControl(string className, long windowStyle)
    {
        if ((windowStyle & EditPasswordStyle) != 0)
        {
            return true;
        }

        return className.Contains("PasswordBox", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Credential", StringComparison.OrdinalIgnoreCase);
    }
}
