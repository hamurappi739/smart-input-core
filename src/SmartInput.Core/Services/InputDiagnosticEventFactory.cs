using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public static class InputDiagnosticEventFactory
{
    public static InputDiagnosticEvent Create(
        KeyEventType eventType,
        int virtualKeyCode,
        string processName,
        string windowTitle,
        string windowClassName,
        bool isMonitoringActive,
        DateTimeOffset timestampUtc)
    {
        return new InputDiagnosticEvent
        {
            EventType = eventType,
            VirtualKeyCode = virtualKeyCode,
            IsMonitoringActive = isMonitoringActive,
            TimestampUtc = timestampUtc,
        };
    }
}
