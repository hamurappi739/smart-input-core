using SmartInput.Core.Models;

namespace SmartInput.App.ViewModels;

public sealed class InputDiagnosticEntryViewModel
{
    public InputDiagnosticEntryViewModel(InputDiagnosticEvent diagnosticEvent)
    {
        Timestamp = diagnosticEvent.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        EventType = diagnosticEvent.EventType.ToString();
        VirtualKeyCode = $"0x{diagnosticEvent.VirtualKeyCode:X2}";
        ProcessName = string.Empty;
        WindowTitle = string.Empty;
        WindowClassName = string.Empty;
        IsMonitoringActive = diagnosticEvent.IsMonitoringActive;
        Summary = $"{Timestamp} · {EventType} · VK {VirtualKeyCode}";
    }

    public string Timestamp { get; }

    public string EventType { get; }

    public string VirtualKeyCode { get; }

    public string ProcessName { get; }

    public string WindowTitle { get; }

    public string WindowClassName { get; }

    public bool IsMonitoringActive { get; }

    public string Summary { get; }
}
