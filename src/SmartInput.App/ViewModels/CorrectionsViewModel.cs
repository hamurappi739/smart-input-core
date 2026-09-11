using CommunityToolkit.Mvvm.ComponentModel;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.ViewModels;

public partial class CorrectionsViewModel : SettingsViewModelBase
{
    public CorrectionsViewModel(ISettingsService settingsService)
        : base(settingsService)
    {
    }

    [ObservableProperty]
    private bool _automaticLayoutEnabled;

    [ObservableProperty]
    private bool _autocorrectEnabled;

    [ObservableProperty]
    private bool _externalSpellingEngineEnabled;

    [ObservableProperty]
    private bool _capitalizationEnabled;

    [ObservableProperty]
    private bool _punctuationEnabled;

    [ObservableProperty]
    private bool _predictionEnabled;

    partial void OnAutomaticLayoutEnabledChanged(bool value)
    {
        PersistSetting(settings => settings.AutomaticLayoutEnabled = value);
    }

    partial void OnAutocorrectEnabledChanged(bool value)
    {
        PersistSetting(settings => settings.AutocorrectEnabled = value);
    }

    partial void OnExternalSpellingEngineEnabledChanged(bool value)
    {
        PersistSetting(settings => settings.ExternalSpellingEngineEnabled = value);
    }

    partial void OnCapitalizationEnabledChanged(bool value)
    {
        PersistSetting(settings => settings.CapitalizationEnabled = value);
    }

    partial void OnPunctuationEnabledChanged(bool value)
    {
        PersistSetting(settings => settings.PunctuationEnabled = value);
    }

    partial void OnPredictionEnabledChanged(bool value)
    {
        PersistSetting(settings => settings.PredictionEnabled = value);
    }

    protected override void SyncFromSettings(AppSettings settings)
    {
        AutomaticLayoutEnabled = settings.AutomaticLayoutEnabled;
        AutocorrectEnabled = settings.AutocorrectEnabled;
        ExternalSpellingEngineEnabled = settings.ExternalSpellingEngineEnabled;
        CapitalizationEnabled = settings.CapitalizationEnabled;
        PunctuationEnabled = settings.PunctuationEnabled;
        PredictionEnabled = settings.PredictionEnabled;
    }
}
