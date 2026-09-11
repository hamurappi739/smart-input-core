using System.Diagnostics;
using System.Text;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsActiveApplicationService : IActiveApplicationService
{
    public Task<ActiveApplicationInfo?> GetActiveApplicationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windowHandle = Win32Window.GetForegroundWindow();
        if (windowHandle == 0)
        {
            return Task.FromResult<ActiveApplicationInfo?>(null);
        }

        var titleBuilder = new StringBuilder(512);
        _ = Win32Window.GetWindowText(windowHandle, titleBuilder, titleBuilder.Capacity);

        var classBuilder = new StringBuilder(256);
        _ = Win32Window.GetClassName(windowHandle, classBuilder, classBuilder.Capacity);

        Win32Window.GetWindowThreadProcessId(windowHandle, out var processId);
        var processName = ResolveProcessName(processId);

        return Task.FromResult<ActiveApplicationInfo?>(new ActiveApplicationInfo(
            processName,
            titleBuilder.ToString(),
            classBuilder.ToString(),
            windowHandle));
    }

    private static string ResolveProcessName(uint processId)
    {
        if (processId == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
