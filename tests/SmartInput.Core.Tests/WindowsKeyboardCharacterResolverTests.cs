using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Services;

namespace SmartInput.Core.Tests;

public sealed class WindowsKeyboardCharacterResolverTests
{
    [Fact]
    public void OverlayPhysicalModifierState_UsesCurrentShiftAndCapsLockState()
    {
        var keyboardState = new byte[256];

        WindowsKeyboardCharacterResolver.OverlayPhysicalModifierState(
            keyboardState,
            virtualKey => virtualKey is VirtualKeys.Shift or VirtualKeys.LShift
                ? unchecked((short)0x8000)
                : (short)0,
            virtualKey => virtualKey == VirtualKeys.Capital ? (short)0x0001 : (short)0);

        Assert.NotEqual(0, keyboardState[VirtualKeys.Shift] & 0x80);
        Assert.NotEqual(0, keyboardState[VirtualKeys.LShift] & 0x80);
        Assert.Equal(0, keyboardState[VirtualKeys.RShift] & 0x80);
        Assert.Equal(0x01, keyboardState[VirtualKeys.Capital] & 0x01);
    }

    [Fact]
    public void OverlayPhysicalModifierState_ClearsStaleShiftFromHookThreadSnapshot()
    {
        var keyboardState = Enumerable.Repeat((byte)0x80, 256).ToArray();

        WindowsKeyboardCharacterResolver.OverlayPhysicalModifierState(
            keyboardState,
            _ => 0,
            _ => 0);

        Assert.Equal(0, keyboardState[VirtualKeys.Shift] & 0x80);
        Assert.Equal(0, keyboardState[VirtualKeys.LShift] & 0x80);
        Assert.Equal(0, keyboardState[VirtualKeys.RShift] & 0x80);
        Assert.Equal(0, keyboardState[VirtualKeys.Capital]);
    }
}
