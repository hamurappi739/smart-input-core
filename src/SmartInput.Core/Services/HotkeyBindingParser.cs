using System.Globalization;
using System.Text;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Services;

public static class HotkeyBindingParser
{
    private static readonly HashSet<string> ReservedCombinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+Alt+Delete",
        "Ctrl+Alt+Del",
        "Alt+F4",
        "Ctrl+Esc",
        "Ctrl+Escape",
        "Win+L",
        "Ctrl+Shift+Esc",
        "Ctrl+Shift+Escape",
        "Alt+Tab",
        "Alt+Esc",
        "Alt+Escape",
        "Ctrl+Alt+Tab",
    };

    public static HotkeyBinding Parse(string? combination)
    {
        if (string.IsNullOrWhiteSpace(combination))
        {
            return Invalid(HotkeyValidationState.Empty, string.Empty, "Горячая клавиша не может быть пустой.");
        }

        var tokens = combination
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return Invalid(HotkeyValidationState.Empty, combination.Trim(), "Горячая клавиша не может быть пустой.");
        }

        var modifiers = HotkeyModifiers.None;
        string? keyToken = null;

        foreach (var token in tokens)
        {
            if (TryParseModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (keyToken is not null)
            {
                return Invalid(
                    HotkeyValidationState.InvalidFormat,
                    combination.Trim(),
                    "Горячая клавиша должна содержать ровно одну клавишу помимо модификаторов.");
            }

            keyToken = token;
        }

        if (keyToken is null)
        {
            return Invalid(
                HotkeyValidationState.ModifierOnly,
                Format(modifiers, keyName: null),
                "Горячая клавиша должна содержать клавишу помимо модификаторов.");
        }

        if (modifiers == HotkeyModifiers.None)
        {
            return Invalid(
                HotkeyValidationState.MissingModifier,
                keyToken,
                "Горячая клавиша должна содержать хотя бы один модификатор: Ctrl, Alt, Shift или Win.");
        }

        if (!TryParseKey(keyToken, out var virtualKey, out var keyDisplayName))
        {
            return Invalid(
                HotkeyValidationState.UnsupportedKey,
                combination.Trim(),
                $"Неподдерживаемая клавиша: «{keyToken}».");
        }

        var displayName = Format(modifiers, keyDisplayName);
        if (ReservedCombinations.Contains(displayName)
            || ReservedCombinations.Contains(combination.Trim()))
        {
            return new HotkeyBinding
            {
                Modifiers = modifiers,
                VirtualKey = virtualKey,
                DisplayName = displayName,
                ValidationState = HotkeyValidationState.ReservedCombination,
                ValidationMessage = $"Сочетание «{displayName}» зарезервировано операционной системой.",
            };
        }

        return new HotkeyBinding
        {
            Modifiers = modifiers,
            VirtualKey = virtualKey,
            DisplayName = displayName,
            ValidationState = HotkeyValidationState.Valid,
        };
    }

    public static string Format(HotkeyModifiers modifiers, string? keyName)
    {
        var builder = new StringBuilder();

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            AppendPart(builder, "Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            AppendPart(builder, "Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            AppendPart(builder, "Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            AppendPart(builder, "Win");
        }

        if (!string.IsNullOrWhiteSpace(keyName))
        {
            AppendPart(builder, keyName);
        }

        return builder.ToString();
    }

    public static string Format(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.DisplayName;
    }

    private static HotkeyBinding Invalid(
        HotkeyValidationState state,
        string displayName,
        string message)
    {
        return new HotkeyBinding
        {
            Modifiers = HotkeyModifiers.None,
            VirtualKey = 0,
            DisplayName = displayName,
            ValidationState = state,
            ValidationMessage = message,
        };
    }

    private static bool TryParseModifier(string token, out HotkeyModifiers modifier)
    {
        switch (token.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = HotkeyModifiers.Control;
                return true;
            case "alt":
            case "menu":
                modifier = HotkeyModifiers.Alt;
                return true;
            case "shift":
                modifier = HotkeyModifiers.Shift;
                return true;
            case "win":
            case "windows":
            case "meta":
                modifier = HotkeyModifiers.Win;
                return true;
            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static bool TryParseKey(string token, out int virtualKey, out string displayName)
    {
        virtualKey = 0;
        displayName = string.Empty;

        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z')
            {
                virtualKey = character;
                displayName = character.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (character is >= '0' and <= '9')
            {
                virtualKey = character;
                displayName = character.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        switch (token.ToLowerInvariant())
        {
            case "backspace":
            case "bksp":
                virtualKey = VirtualKeys.Backspace;
                displayName = "Backspace";
                return true;
            case "tab":
                virtualKey = VirtualKeys.Tab;
                displayName = "Tab";
                return true;
            case "enter":
            case "return":
                virtualKey = VirtualKeys.Return;
                displayName = "Enter";
                return true;
            case "escape":
            case "esc":
                virtualKey = VirtualKeys.Escape;
                displayName = "Esc";
                return true;
            case "space":
            case "spacebar":
                virtualKey = VirtualKeys.Space;
                displayName = "Space";
                return true;
            case "delete":
            case "del":
                virtualKey = VirtualKeys.Delete;
                displayName = "Delete";
                return true;
            case "insert":
            case "ins":
                virtualKey = VirtualKeys.Insert;
                displayName = "Insert";
                return true;
            case "home":
                virtualKey = VirtualKeys.Home;
                displayName = "Home";
                return true;
            case "end":
                virtualKey = VirtualKeys.End;
                displayName = "End";
                return true;
            case "pageup":
            case "pgup":
                virtualKey = VirtualKeys.Prior;
                displayName = "PageUp";
                return true;
            case "pagedown":
            case "pgdn":
                virtualKey = VirtualKeys.Next;
                displayName = "PageDown";
                return true;
            case "left":
                virtualKey = VirtualKeys.Left;
                displayName = "Left";
                return true;
            case "right":
                virtualKey = VirtualKeys.Right;
                displayName = "Right";
                return true;
            case "up":
                virtualKey = VirtualKeys.Up;
                displayName = "Up";
                return true;
            case "down":
                virtualKey = VirtualKeys.Down;
                displayName = "Down";
                return true;
            case "pause":
            case "break":
                virtualKey = VirtualKeys.Pause;
                displayName = "Pause";
                return true;
        }

        if (token.Length >= 2
            && (token[0] is 'F' or 'f')
            && int.TryParse(token[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            virtualKey = 0x70 + (functionNumber - 1);
            displayName = "F" + functionNumber.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static void AppendPart(StringBuilder builder, string part)
    {
        if (builder.Length > 0)
        {
            builder.Append('+');
        }

        builder.Append(part);
    }
}
