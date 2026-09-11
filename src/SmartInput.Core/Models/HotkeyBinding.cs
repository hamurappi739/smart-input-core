namespace SmartInput.Core.Models;

public sealed class HotkeyBinding
{
    public required HotkeyModifiers Modifiers { get; init; }

    public required int VirtualKey { get; init; }

    public required string DisplayName { get; init; }

    public required HotkeyValidationState ValidationState { get; init; }

    public string? ValidationMessage { get; init; }

    public bool IsValid => ValidationState == HotkeyValidationState.Valid;
}
