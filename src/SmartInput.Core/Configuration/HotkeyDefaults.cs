namespace SmartInput.Core.Configuration;

public static class HotkeyDefaults
{
    public const string ToggleAutomaticLayoutId = "toggle-automatic-layout";

    public const string ToggleAutomaticLayoutHotkey = "Ctrl+F12";

    public const string UndoLastCorrectionId = "undo-last-correction";

    /// <summary>
    /// Rare triple-modifier chord that avoids common Windows, browser, IDE, and terminal shortcuts.
    /// </summary>
    public const string UndoLastCorrectionHotkey = "Ctrl+Alt+Shift+Z";

    public const string FixLayoutEnToRuId = "fix-layout-en-ru";

    public const string FixLayoutEnToRuHotkey = "Ctrl+Alt+Shift+L";

    public const string FixLayoutRuToEnId = "fix-layout-ru-en";

    public const string FixLayoutRuToEnHotkey = "Ctrl+Alt+Shift+K";

    public const string FixSpellingId = "fix-spelling";

    public const string FixSpellingHotkey = "Ctrl+Alt+Shift+S";

    public const string FixTextEnToRuId = "fix-text-en-ru";

    public const string FixTextEnToRuHotkey = "Ctrl+Alt+Shift+T";

    public const string FixTextRuToEnId = "fix-text-ru-en";

    public const string FixTextRuToEnHotkey = "Ctrl+Alt+Shift+Y";
}
