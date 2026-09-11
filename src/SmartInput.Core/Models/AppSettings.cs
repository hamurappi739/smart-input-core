namespace SmartInput.Core.Models;

public sealed class AppSettings
{
    public bool IsEnabled { get; set; } = true;

    public bool EmergencyPauseEnabled { get; set; }

    public bool AutomaticLayoutEnabled { get; set; } = true;

    /// <summary>
    /// Global shortcut that turns only automatic layout correction on or off.
    /// Manual correction shortcuts remain available.
    /// </summary>
    public string ToggleAutomaticLayoutHotkey { get; set; } = "Ctrl+F12";

    /// <summary>
    /// Maximum interval between two released Shift taps that forms the
    /// deterministic manual toggle/undo command. Kept in configuration so a
    /// keyboard layout or accessibility setup can tune it without changing
    /// the correction model.
    /// </summary>
    public int DoubleShiftWindowMilliseconds { get; set; } = 350;

    public bool AutocorrectEnabled { get; set; }

    /// <summary>
    /// Opt-in use of the local Hunspell/SymSpell spelling provider.
    /// Disabled by default until the user validates the experimental mode.
    /// </summary>
    public bool ExternalSpellingEngineEnabled { get; set; }

    public bool SnippetsEnabled { get; set; }

    /// <summary>
    /// Experimental next-word suggestions are opt-in and are not part of the
    /// normal Smart Input workflow.
    /// </summary>
    public bool PredictionEnabled { get; set; }

    public bool CapitalizationEnabled { get; set; } = true;

    public bool PunctuationEnabled { get; set; }

    /// <summary>
    /// Uses the dark application appearance. This affects only Smart Input's UI;
    /// it never changes the Windows theme or the active keyboard layout.
    /// </summary>
    public bool UseDarkTheme { get; set; }

    public bool StartWithWindows { get; set; } = true;

    public bool PerformanceDebugLoggingEnabled { get; set; }

    public string UndoLastCorrectionHotkey { get; set; } = "Ctrl+Alt+Shift+Z";

    public string FixLayoutEnToRuHotkey { get; set; } = "Ctrl+Alt+Shift+L";

    public string FixLayoutRuToEnHotkey { get; set; } = "Ctrl+Alt+Shift+K";

    public string FixSpellingHotkey { get; set; } = "Ctrl+Alt+Shift+S";

    public string FixTextEnToRuHotkey { get; set; } = "Ctrl+Alt+Shift+T";

    public string FixTextRuToEnHotkey { get; set; } = "Ctrl+Alt+Shift+Y";

    public List<string> ExcludedApplications { get; set; } = [];
}
