using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public class HotkeyBindingParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+Z", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 'Z')]
    [InlineData("ctrl+alt+shift+z", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 'Z')]
    [InlineData("Control+Alt+U", HotkeyModifiers.Control | HotkeyModifiers.Alt, 'U')]
    [InlineData("Win+Shift+F12", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x7B)]
    [InlineData("Ctrl+Backspace", HotkeyModifiers.Control, VirtualKeys.Backspace)]
    public void Parse_ValidCombinations_ReturnsNormalizedBinding(
        string input,
        HotkeyModifiers expectedModifiers,
        int expectedVirtualKey)
    {
        var binding = HotkeyBindingParser.Parse(input);

        Assert.True(binding.IsValid);
        Assert.Equal(HotkeyValidationState.Valid, binding.ValidationState);
        Assert.Equal(expectedModifiers, binding.Modifiers);
        Assert.Equal(expectedVirtualKey, binding.VirtualKey);
    }

    [Fact]
    public void Parse_DefaultUndoHotkey_IsValid()
    {
        var binding = HotkeyBindingParser.Parse(HotkeyDefaults.UndoLastCorrectionHotkey);

        Assert.True(binding.IsValid);
        Assert.Equal("Ctrl+Alt+Shift+Z", binding.DisplayName);
    }

    [Theory]
    [InlineData(HotkeyDefaults.FixLayoutEnToRuHotkey)]
    [InlineData(HotkeyDefaults.FixLayoutRuToEnHotkey)]
    [InlineData(HotkeyDefaults.FixSpellingHotkey)]
    [InlineData(HotkeyDefaults.FixTextEnToRuHotkey)]
    [InlineData(HotkeyDefaults.FixTextRuToEnHotkey)]
    public void Parse_DefaultManualCorrectionHotkeys_AreValid(string combination)
    {
        var binding = HotkeyBindingParser.Parse(combination);

        Assert.True(binding.IsValid);
        Assert.Equal(HotkeyValidationState.Valid, binding.ValidationState);
    }

    [Fact]
    public void Format_OrdersModifiersConsistently()
    {
        var display = HotkeyBindingParser.Format(
            HotkeyModifiers.Shift | HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Alt,
            "Z");

        Assert.Equal("Ctrl+Alt+Shift+Win+Z", display);
    }

    [Theory]
    [InlineData(null, HotkeyValidationState.Empty)]
    [InlineData("", HotkeyValidationState.Empty)]
    [InlineData("   ", HotkeyValidationState.Empty)]
    [InlineData("Ctrl+Alt", HotkeyValidationState.ModifierOnly)]
    [InlineData("Z", HotkeyValidationState.MissingModifier)]
    [InlineData("Ctrl+Alt+Delete", HotkeyValidationState.ReservedCombination)]
    [InlineData("Alt+F4", HotkeyValidationState.ReservedCombination)]
    [InlineData("Ctrl+Esc", HotkeyValidationState.ReservedCombination)]
    [InlineData("Ctrl+Alt+Shift+Foo", HotkeyValidationState.UnsupportedKey)]
    [InlineData("Ctrl+Alt+Z+X", HotkeyValidationState.InvalidFormat)]
    public void Parse_InvalidCombinations_Rejected(string? input, HotkeyValidationState expectedState)
    {
        var binding = HotkeyBindingParser.Parse(input);

        Assert.False(binding.IsValid);
        Assert.Equal(expectedState, binding.ValidationState);
        Assert.False(string.IsNullOrWhiteSpace(binding.ValidationMessage));
    }
}
