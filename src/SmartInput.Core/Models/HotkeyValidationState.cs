namespace SmartInput.Core.Models;

public enum HotkeyValidationState
{
    Valid,
    Empty,
    InvalidFormat,
    ModifierOnly,
    MissingModifier,
    ReservedCombination,
    UnsupportedKey,
}
