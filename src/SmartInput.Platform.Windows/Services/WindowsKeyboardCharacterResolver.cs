using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsKeyboardCharacterResolver : IKeyboardCharacterResolver
{
    private static readonly int[] PotentialBoundaryVirtualKeys =
    [
        VirtualKeys.Oem1,
        VirtualKeys.OemComma,
        VirtualKeys.OemPeriod,
        VirtualKeys.Oem2,
        VirtualKeys.Oem3,
        VirtualKeys.Oem4,
        VirtualKeys.Oem5,
        VirtualKeys.Oem6,
        VirtualKeys.Oem7,
        VirtualKeys.OemMinus,
        VirtualKeys.OemPlus,
    ];

    private static readonly int[] ResetVirtualKeys =
    [
        VirtualKeys.Escape,
        VirtualKeys.Insert,
        VirtualKeys.Delete,
        VirtualKeys.Home,
        VirtualKeys.End,
        VirtualKeys.Prior,
        VirtualKeys.Next,
        VirtualKeys.Left,
        VirtualKeys.Right,
        VirtualKeys.Up,
        VirtualKeys.Down,
    ];

    public KeyboardCharacterResolution Resolve(KeyboardObservationEventArgs observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.EventType != KeyEventType.KeyDown)
        {
            return KeyboardCharacterResolution.Ignored();
        }

        var virtualKey = observation.VirtualKeyCode;

        if (IsModifierVirtualKey(virtualKey))
        {
            return KeyboardCharacterResolution.Ignored();
        }

        if (HasBlockingModifier())
        {
            return KeyboardCharacterResolution.Reset();
        }

        if (virtualKey == VirtualKeys.Backspace)
        {
            return KeyboardCharacterResolution.Backspace();
        }

        if (InputCharacterClassification.IsExplicitBoundaryVirtualKey(virtualKey))
        {
            return CreateVirtualKeyBoundary(virtualKey, observation.ScanCode);
        }

        if (Array.IndexOf(PotentialBoundaryVirtualKeys, virtualKey) >= 0)
        {
            return ResolveOemOrPunctuation(virtualKey, observation.ScanCode);
        }

        if (Array.IndexOf(ResetVirtualKeys, virtualKey) >= 0)
        {
            return KeyboardCharacterResolution.Reset();
        }

        return ResolveCharacter(virtualKey, observation.ScanCode);
    }

    private static KeyboardCharacterResolution CreateVirtualKeyBoundary(int virtualKey, int scanCode)
    {
        return KeyboardCharacterResolution.CreateBoundary(
            DeferredBoundaryKey.FromVirtualKey(virtualKey, scanCode));
    }

    private static KeyboardCharacterResolution ResolveOemOrPunctuation(int virtualKey, int scanCode)
    {
        var character = TryResolveUnicodeCharacter(virtualKey, scanCode);
        if (character is null)
        {
            // Never defer an unresolved OEM punctuation key as a virtual key:
            // the active layout may change while correction is evaluated, and
            // replaying that VK later can turn `,` into another character.
            // Fail open so Windows delivers the original physical key using
            // the layout that was active at the time of the press.
            return KeyboardCharacterResolution.Uncertain();
        }

        // A comma key must never be replayed as a period (or vice versa).
        // If the layout API reports a cross-mapped punctuation character,
        // fail open and let the original event reach the target unchanged.
        if ((virtualKey == VirtualKeys.OemComma && character == '.')
            || (virtualKey == VirtualKeys.OemPeriod && character == ','))
        {
            return KeyboardCharacterResolution.Uncertain();
        }

        if (InputCharacterClassification.IsTrackableTriggerCharacter(character.Value))
        {
            return KeyboardCharacterResolution.CharacterOf(character.Value);
        }

        if (InputCharacterClassification.IsWordBoundaryCharacter(character.Value))
        {
            return KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromUnicodeCharacter(virtualKey, scanCode, character.Value));
        }

        return CreateVirtualKeyBoundary(virtualKey, scanCode);
    }

    private static KeyboardCharacterResolution ResolveCharacter(int virtualKey, int scanCode)
    {
        var character = TryResolveUnicodeCharacter(virtualKey, scanCode);
        if (character is null)
        {
            return KeyboardCharacterResolution.Ignored();
        }

        if (InputCharacterClassification.IsTrackableTriggerCharacter(character.Value))
        {
            return KeyboardCharacterResolution.CharacterOf(character.Value);
        }

        if (InputCharacterClassification.IsWordBoundaryCharacter(character.Value))
        {
            return KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromUnicodeCharacter(virtualKey, scanCode, character.Value));
        }

        return KeyboardCharacterResolution.Reset();
    }

    private static char? TryResolveUnicodeCharacter(int virtualKey, int scanCode)
    {
        var keyboardState = new byte[256];
        if (!Win32Keyboard.GetKeyboardState(keyboardState))
        {
            return null;
        }

        // This resolver runs on the hook/message thread, not necessarily on
        // the foreground application's UI thread.  GetKeyboardState can then
        // contain a stale modifier snapshot.  In particular a shifted Russian
        // punctuation key can be resolved as '.' instead of ','.  Overlay the
        // physical modifier state at the instant of the hook callback before
        // calling ToUnicodeEx, while retaining the toggle-state bits supplied
        // by GetKeyboardState.
        OverlayPhysicalModifierState(
            keyboardState,
            Win32Keyboard.GetAsyncKeyState,
            Win32Keyboard.GetKeyState);

        var foregroundWindow = Win32Window.GetForegroundWindow();
        if (foregroundWindow == 0)
        {
            return null;
        }

        var threadId = Win32Window.GetWindowThreadProcessId(foregroundWindow, out _);
        if (threadId == 0)
        {
            return null;
        }

        var keyboardLayout = Win32Keyboard.GetKeyboardLayout(threadId);
        if (keyboardLayout == 0)
        {
            return null;
        }

        var buffer = new char[2];
        var result = Win32Keyboard.ToUnicodeEx(
            (uint)virtualKey,
            (uint)scanCode,
            keyboardState,
            buffer,
            buffer.Length,
            0,
            keyboardLayout);

        if (result <= 0)
        {
            return null;
        }

        return buffer[0];
    }

    internal static void OverlayPhysicalModifierState(
        byte[] keyboardState,
        Func<int, short> getAsyncKeyState,
        Func<int, short> getKeyState)
    {
        ArgumentNullException.ThrowIfNull(keyboardState);
        ArgumentNullException.ThrowIfNull(getAsyncKeyState);
        ArgumentNullException.ThrowIfNull(getKeyState);

        ApplyPhysicalModifierState(keyboardState, VirtualKeys.Shift, getAsyncKeyState);
        ApplyPhysicalModifierState(keyboardState, VirtualKeys.LShift, getAsyncKeyState);
        ApplyPhysicalModifierState(keyboardState, VirtualKeys.RShift, getAsyncKeyState);
        ApplyPhysicalModifierState(keyboardState, VirtualKeys.Control, getAsyncKeyState);
        ApplyPhysicalModifierState(keyboardState, VirtualKeys.Menu, getAsyncKeyState);
        ApplyCapsLockState(keyboardState, getKeyState);
    }

    private static void ApplyPhysicalModifierState(
        byte[] keyboardState,
        int virtualKey,
        Func<int, short> getAsyncKeyState)
    {
        var isDown = (getAsyncKeyState(virtualKey) & 0x8000) != 0;
        if (isDown)
        {
            keyboardState[virtualKey] |= 0x80;
        }
        else
        {
            keyboardState[virtualKey] &= 0x7F;
        }
    }

    private static void ApplyCapsLockState(byte[] keyboardState, Func<int, short> getKeyState)
    {
        var state = getKeyState(VirtualKeys.Capital);
        keyboardState[VirtualKeys.Capital] = (byte)(state & 0x0001);
        if ((state & 0x8000) != 0)
        {
            keyboardState[VirtualKeys.Capital] |= 0x80;
        }
    }

    private static bool HasBlockingModifier()
    {
        return IsKeyDown(VirtualKeys.Control)
            || IsKeyDown(VirtualKeys.Menu)
            || IsKeyDown(VirtualKeys.LWin)
            || IsKeyDown(VirtualKeys.RWin);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        var state = Win32Keyboard.GetAsyncKeyState(virtualKey);
        return (state & 0x8000) != 0;
    }

    private static bool IsModifierVirtualKey(int virtualKey)
    {
        return virtualKey is VirtualKeys.Shift
            or VirtualKeys.LShift
            or VirtualKeys.RShift
            or VirtualKeys.Control
            or VirtualKeys.Menu
            or VirtualKeys.Capital
            or VirtualKeys.LWin
            or VirtualKeys.RWin;
    }
}
