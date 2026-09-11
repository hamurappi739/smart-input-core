using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class InputDiagnosticEventFactoryTests
{
    [Fact]
    public void Create_RedactsTargetMetadata()
    {
        var timestamp = new DateTimeOffset(2026, 8, 30, 10, 15, 0, TimeSpan.Zero);

        var diagnostic = InputDiagnosticEventFactory.Create(
            KeyEventType.KeyUp,
            virtualKeyCode: 0x1B,
            processName: "explorer",
            windowTitle: "Desktop",
            windowClassName: "Progman",
            isMonitoringActive: true,
            timestamp);

        Assert.Equal(KeyEventType.KeyUp, diagnostic.EventType);
        Assert.Equal(0x1B, diagnostic.VirtualKeyCode);
        Assert.Equal(string.Empty, diagnostic.ProcessName);
        Assert.Equal(string.Empty, diagnostic.WindowTitle);
        Assert.Equal(string.Empty, diagnostic.WindowClassName);
        Assert.True(diagnostic.IsMonitoringActive);
        Assert.Equal(timestamp, diagnostic.TimestampUtc);
    }

    [Fact]
    public void Create_UsesEmptyStringsForNullMetadata()
    {
        var diagnostic = InputDiagnosticEventFactory.Create(
            KeyEventType.KeyDown,
            virtualKeyCode: 0x10,
            processName: null!,
            windowTitle: null!,
            windowClassName: null!,
            isMonitoringActive: false,
            DateTimeOffset.UtcNow);

        Assert.Equal(string.Empty, diagnostic.ProcessName);
        Assert.Equal(string.Empty, diagnostic.WindowTitle);
        Assert.Equal(string.Empty, diagnostic.WindowClassName);
        Assert.False(diagnostic.IsMonitoringActive);
    }
}
