using SmartInput.Core.Configuration;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void DefaultSettings_EnableCoreFeaturesByDefault()
    {
        var settings = SettingsDefaults.CreateDefault();

        Assert.True(settings.IsEnabled);
        Assert.True(settings.AutomaticLayoutEnabled);
        Assert.Equal(HotkeyDefaults.ToggleAutomaticLayoutHotkey, settings.ToggleAutomaticLayoutHotkey);
        Assert.False(settings.AutocorrectEnabled);
        Assert.False(settings.SnippetsEnabled);
        Assert.False(settings.PredictionEnabled);
        Assert.True(settings.CapitalizationEnabled);
        Assert.False(settings.PunctuationEnabled);
        Assert.True(settings.StartWithWindows);
        Assert.Equal("Ctrl+Alt+Shift+Z", settings.UndoLastCorrectionHotkey);
        Assert.Equal(HotkeyDefaults.FixLayoutEnToRuHotkey, settings.FixLayoutEnToRuHotkey);
        Assert.Equal(HotkeyDefaults.FixLayoutRuToEnHotkey, settings.FixLayoutRuToEnHotkey);
        Assert.Equal(HotkeyDefaults.FixSpellingHotkey, settings.FixSpellingHotkey);
        Assert.Equal(HotkeyDefaults.FixTextEnToRuHotkey, settings.FixTextEnToRuHotkey);
        Assert.Equal(HotkeyDefaults.FixTextRuToEnHotkey, settings.FixTextRuToEnHotkey);
    }
}
