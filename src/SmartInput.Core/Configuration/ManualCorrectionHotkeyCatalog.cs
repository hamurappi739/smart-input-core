using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Configuration;

public sealed class ManualCorrectionHotkeyDefinition
{
    public required string HotkeyId { get; init; }

    public required string ActionLabel { get; init; }

    public required string DefaultHotkey { get; init; }

    public required ManualCorrectionActionKind Action { get; init; }

    public LayoutConversionDirection? LayoutDirection { get; init; }
}

public static class ManualCorrectionHotkeyCatalog
{
    public static IReadOnlyList<ManualCorrectionHotkeyDefinition> Definitions { get; } =
    [
        new()
        {
            HotkeyId = HotkeyDefaults.FixLayoutEnToRuId,
            ActionLabel = "Исправить раскладку: EN → RU",
            DefaultHotkey = HotkeyDefaults.FixLayoutEnToRuHotkey,
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        },
        new()
        {
            HotkeyId = HotkeyDefaults.FixLayoutRuToEnId,
            ActionLabel = "Исправить раскладку: RU → EN",
            DefaultHotkey = HotkeyDefaults.FixLayoutRuToEnHotkey,
            Action = ManualCorrectionActionKind.FixLayout,
            LayoutDirection = LayoutConversionDirection.RussianToEnglish,
        },
        new()
        {
            HotkeyId = HotkeyDefaults.FixSpellingId,
            ActionLabel = "Исправить орфографию",
            DefaultHotkey = HotkeyDefaults.FixSpellingHotkey,
            Action = ManualCorrectionActionKind.FixSpelling,
        },
        new()
        {
            HotkeyId = HotkeyDefaults.FixTextEnToRuId,
            ActionLabel = "Исправить текст: EN → RU",
            DefaultHotkey = HotkeyDefaults.FixTextEnToRuHotkey,
            Action = ManualCorrectionActionKind.FixText,
            LayoutDirection = LayoutConversionDirection.EnglishToRussian,
        },
        new()
        {
            HotkeyId = HotkeyDefaults.FixTextRuToEnId,
            ActionLabel = "Исправить текст: RU → EN",
            DefaultHotkey = HotkeyDefaults.FixTextRuToEnHotkey,
            Action = ManualCorrectionActionKind.FixText,
            LayoutDirection = LayoutConversionDirection.RussianToEnglish,
        },
    ];

    public static ManualCorrectionHotkeyDefinition? TryGetDefinition(string hotkeyId)
    {
        foreach (var definition in Definitions)
        {
            if (string.Equals(definition.HotkeyId, hotkeyId, StringComparison.Ordinal))
            {
                return definition;
            }
        }

        return null;
    }

    public static ManualCorrectionRequest CreateRequest(string hotkeyId)
    {
        var definition = TryGetDefinition(hotkeyId)
            ?? throw new ArgumentException($"Unknown manual correction hotkey id '{hotkeyId}'.", nameof(hotkeyId));

        return new ManualCorrectionRequest
        {
            Action = definition.Action,
            LayoutDirection = definition.LayoutDirection,
        };
    }

    public static string GetConfiguredHotkey(AppSettings settings, string hotkeyId)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return hotkeyId switch
        {
            HotkeyDefaults.FixLayoutEnToRuId => settings.FixLayoutEnToRuHotkey,
            HotkeyDefaults.FixLayoutRuToEnId => settings.FixLayoutRuToEnHotkey,
            HotkeyDefaults.FixSpellingId => settings.FixSpellingHotkey,
            HotkeyDefaults.FixTextEnToRuId => settings.FixTextEnToRuHotkey,
            HotkeyDefaults.FixTextRuToEnId => settings.FixTextRuToEnHotkey,
            _ => throw new ArgumentException($"Unknown manual correction hotkey id '{hotkeyId}'.", nameof(hotkeyId)),
        };
    }

    public static void SetConfiguredHotkey(AppSettings settings, string hotkeyId, string value)
    {
        ArgumentNullException.ThrowIfNull(settings);

        switch (hotkeyId)
        {
            case HotkeyDefaults.FixLayoutEnToRuId:
                settings.FixLayoutEnToRuHotkey = value;
                break;
            case HotkeyDefaults.FixLayoutRuToEnId:
                settings.FixLayoutRuToEnHotkey = value;
                break;
            case HotkeyDefaults.FixSpellingId:
                settings.FixSpellingHotkey = value;
                break;
            case HotkeyDefaults.FixTextEnToRuId:
                settings.FixTextEnToRuHotkey = value;
                break;
            case HotkeyDefaults.FixTextRuToEnId:
                settings.FixTextRuToEnHotkey = value;
                break;
            default:
                throw new ArgumentException($"Unknown manual correction hotkey id '{hotkeyId}'.", nameof(hotkeyId));
        }
    }

    public static string ResolveConfiguredHotkey(AppSettings settings, ManualCorrectionHotkeyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(definition);

        var configured = GetConfiguredHotkey(settings, definition.HotkeyId);
        return string.IsNullOrWhiteSpace(configured) ? definition.DefaultHotkey : configured;
    }
}
