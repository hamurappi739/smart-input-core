using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class InputDiagnosticBufferTests
{
    [Fact]
    public void Add_EvictsOldestEventsWhenCapacityExceeded()
    {
        var buffer = new InputDiagnosticBuffer(capacity: 2);

        buffer.Add(CreateEvent(1));
        buffer.Add(CreateEvent(2));
        buffer.Add(CreateEvent(3));

        var snapshot = buffer.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(2, snapshot[0].VirtualKeyCode);
        Assert.Equal(3, snapshot[1].VirtualKeyCode);
    }

    [Fact]
    public void Clear_RemovesAllEvents()
    {
        var buffer = new InputDiagnosticBuffer();
        buffer.Add(CreateEvent(65));
        buffer.Add(CreateEvent(66));

        buffer.Clear();

        Assert.Empty(buffer.Snapshot());
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void Snapshot_ReturnsCopy()
    {
        var buffer = new InputDiagnosticBuffer();
        buffer.Add(CreateEvent(65));

        var firstSnapshot = buffer.Snapshot();
        buffer.Clear();

        Assert.Single(firstSnapshot);
        Assert.Empty(buffer.Snapshot());
    }

    private static InputDiagnosticEvent CreateEvent(int virtualKeyCode)
    {
        return InputDiagnosticEventFactory.Create(
            KeyEventType.KeyDown,
            virtualKeyCode,
            "notepad",
            "Untitled",
            "Notepad",
            isMonitoringActive: true,
            DateTimeOffset.UtcNow);
    }
}
