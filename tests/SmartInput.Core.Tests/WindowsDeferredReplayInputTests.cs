using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;
using SmartInput.Platform.Windows.Services;

namespace SmartInput.Core.Tests;

public sealed class WindowsDeferredReplayInputTests
{
    [Fact]
    public void EarlyCorrectionTriggerKey_IsNotDeferredAsFutureInput()
    {
        Assert.False(WindowsInputMonitor.ShouldDeferAfterInterception(
            KeyEventType.KeyDown,
            isEarlyLayoutCorrectionTrigger: true,
            barrierRequestsDeferral: true));
        Assert.True(WindowsInputMonitor.ShouldDeferAfterInterception(
            KeyEventType.KeyDown,
            isEarlyLayoutCorrectionTrigger: false,
            barrierRequestsDeferral: true));
        Assert.False(WindowsInputMonitor.ShouldDeferAfterInterception(
            KeyEventType.KeyUp,
            isEarlyLayoutCorrectionTrigger: false,
            barrierRequestsDeferral: true));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void EarlyBarrier_ReleasesOnlyAfterTransactionIsFinished(
        bool replacementActive,
        bool suppressedBoundaryPending,
        bool earlyCorrectionPending,
        bool expected)
    {
        Assert.Equal(expected, WindowsInputMonitor.CanReleaseCompletedEarlyBarrier(
            replacementActive,
            suppressedBoundaryPending,
            earlyCorrectionPending));
    }

    [Fact]
    public void DeferredCharacterPair_ReplaysAsCapturedUnicode_NotCurrentVirtualKeyLayout()
    {
        var events = new[]
        {
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = 0x41,
                ScanCode = 30,
                EventType = KeyEventType.KeyDown,
                ResolvedCharacter = 'ф',
            },
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = 0x41,
                ScanCode = 30,
                EventType = KeyEventType.KeyUp,
            },
        };

        var inputs = WindowsInputMonitor.BuildDeferredReplayInputs(events);

        Assert.Equal(2, inputs.Length);
        Assert.Equal(Win32Input.InputKeyboard, inputs[0].Type);
        Assert.Equal((ushort)0, inputs[0].Data.Keyboard.VirtualKey);
        Assert.Equal('ф', (char)inputs[0].Data.Keyboard.ScanCode);
        Assert.Equal(Win32Input.KeyeventfUnicode, inputs[0].Data.Keyboard.Flags);
        Assert.Equal('ф', (char)inputs[1].Data.Keyboard.ScanCode);
        Assert.Equal(
            Win32Input.KeyeventfUnicode | Win32Input.KeyeventfKeyUp,
            inputs[1].Data.Keyboard.Flags);
    }

    [Fact]
    public void DeferredVirtualKeyPair_StaysVirtualForModifiersAndNavigation()
    {
        var events = new[]
        {
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = VirtualKeys.Shift,
                ScanCode = 42,
                EventType = KeyEventType.KeyDown,
            },
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = VirtualKeys.Shift,
                ScanCode = 42,
                EventType = KeyEventType.KeyUp,
            },
        };

        var inputs = WindowsInputMonitor.BuildDeferredReplayInputs(events);

        Assert.Equal((ushort)VirtualKeys.Shift, inputs[0].Data.Keyboard.VirtualKey);
        Assert.Equal((ushort)VirtualKeys.Shift, inputs[1].Data.Keyboard.VirtualKey);
        Assert.Equal(0u, inputs[0].Data.Keyboard.Flags);
        Assert.Equal(Win32Input.KeyeventfKeyUp, inputs[1].Data.Keyboard.Flags);
    }

    [Fact]
    public void DeferredEarlyLayoutKeyWithoutCapturedCharacter_ReplaysPhysicalPair()
    {
        var events = new[]
        {
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = 0x54,
                ScanCode = 20,
                EventType = KeyEventType.KeyDown,
            },
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = 0x54,
                ScanCode = 20,
                EventType = KeyEventType.KeyUp,
            },
        };

        var inputs = WindowsInputMonitor.BuildDeferredReplayInputs(events);

        Assert.Equal((ushort)0x54, inputs[0].Data.Keyboard.VirtualKey);
        Assert.Equal((ushort)0x54, inputs[1].Data.Keyboard.VirtualKey);
        Assert.Equal(0u, inputs[0].Data.Keyboard.Flags);
        Assert.Equal(Win32Input.KeyeventfKeyUp, inputs[1].Data.Keyboard.Flags);
    }
}
