namespace SmartInput.Core.Models;

public sealed class InputDiagnosticEvent
{
    public KeyEventType EventType { get; init; }

    public int VirtualKeyCode { get; init; }

    // Diagnostic events are deliberately unable to carry target metadata.
    // Process names, window titles and window classes remain available to the
    // in-memory safety policy, never to diagnostic buffers, UI or logs.
    public string ProcessName => string.Empty;

    public string WindowTitle => string.Empty;

    public string WindowClassName => string.Empty;

    public bool IsMonitoringActive { get; init; }

    public DateTimeOffset TimestampUtc { get; init; }
}

public enum KeyEventType
{
    KeyDown,
    KeyUp,
}
