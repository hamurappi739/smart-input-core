using System.Runtime.InteropServices;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsBoundaryKeyDeliveryService : IBoundaryKeyDeliveryService
{
    private readonly IPerformanceMetricsRecorder? _performanceMetrics;

    public WindowsBoundaryKeyDeliveryService(IPerformanceMetricsRecorder? performanceMetrics = null)
    {
        _performanceMetrics = performanceMetrics;
    }

    public Task DeliverAsync(DeferredBoundaryKey boundary, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (boundary.DeliveryKind)
        {
            case BoundaryDeliveryKind.VirtualKey:
                SendVirtualKey((ushort)boundary.VirtualKeyCode);
                break;
            case BoundaryDeliveryKind.UnicodeCharacter when boundary.Character is char character:
                SendUnicodeCharacter(character);
                break;
            default:
                SendVirtualKey((ushort)boundary.VirtualKeyCode);
                break;
        }

        _performanceMetrics?.RecordLivePipelineStage(LivePipelineStage.BoundaryDelivered);

        return Task.CompletedTask;
    }

    private static void SendVirtualKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey, scanCode: 0, flags: 0),
            CreateKeyboardInput(virtualKey, scanCode: 0, flags: Win32Input.KeyeventfKeyUp),
        };

        if (Win32Input.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Input.Input>()) != inputs.Length)
        {
            throw new InvalidOperationException("SendInput failed for deferred boundary delivery.");
        }
    }

    private static void SendUnicodeCharacter(char character)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey: 0, scanCode: character, flags: Win32Input.KeyeventfUnicode),
            CreateKeyboardInput(virtualKey: 0, scanCode: character, flags: Win32Input.KeyeventfUnicode | Win32Input.KeyeventfKeyUp),
        };

        if (Win32Input.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Input.Input>()) != inputs.Length)
        {
            throw new InvalidOperationException("SendInput failed for deferred boundary unicode delivery.");
        }
    }

    private static Win32Input.Input CreateKeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new Win32Input.Input
        {
            Type = Win32Input.InputKeyboard,
            Data = new Win32Input.InputUnion
            {
                Keyboard = new Win32Input.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    ExtraInfo = SmartInputInjectionMarkers.SmartInputExtraInfo,
                },
            },
        };
    }
}
