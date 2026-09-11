namespace SmartInput.Core.Services;

public interface ICorrectionApplicationContext
{
    string? ProcessName { get; }

    nint WindowHandle { get; }

    void Update(string? processName, nint windowHandle);
}

public sealed class CorrectionApplicationContext : ICorrectionApplicationContext
{
    private readonly object _sync = new();

    public string? ProcessName { get; private set; }

    public nint WindowHandle { get; private set; }

    public void Update(string? processName, nint windowHandle)
    {
        lock (_sync)
        {
            ProcessName = processName;
            WindowHandle = windowHandle;
        }
    }
}
