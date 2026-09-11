namespace SmartInput.Platform.Abstractions.Input;

/// <summary>
/// Reads the physical modifier state at the point where an injected
/// replacement is about to be sent. This is separate from the hook event
/// queue because Windows may update GetAsyncKeyState slightly after the
/// low-level KeyUp callback is observed.
/// </summary>
public interface IKeyboardModifierState
{
    bool IsShiftDown { get; }
}
