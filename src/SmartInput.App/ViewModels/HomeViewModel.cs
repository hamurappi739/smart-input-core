using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.App.Services;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.ViewModels;

public partial class HomeViewModel : SettingsViewModelBase
{
    private readonly IInputDiagnosticCoordinator _diagnosticCoordinator;
    private readonly IAutomationSafetyService _automationSafetyService;

    public HomeViewModel(
        ISettingsService settingsService,
        IInputDiagnosticCoordinator diagnosticCoordinator,
        IAutomationSafetyService automationSafetyService)
        : base(settingsService)
    {
        _diagnosticCoordinator = diagnosticCoordinator;
        _automationSafetyService = automationSafetyService;
    }

    [ObservableProperty]
    private bool _isProtectionEnabled;

    [ObservableProperty]
    private bool _emergencyPauseEnabled;

    [ObservableProperty]
    private bool _automaticLayoutEnabled;

    [ObservableProperty]
    private bool _autocorrectEnabled;

    [ObservableProperty]
    private bool _snippetsEnabled;

    [ObservableProperty]
    private string _safetyHeadline = "Статус безопасности недоступен";

    [ObservableProperty]
    private string _safetyDetail = string.Empty;

    [ObservableProperty]
    private string _safetyLevelLabel = "Неизвестно";

    [ObservableProperty]
    private bool _isMonitoringActive;

    [ObservableProperty]
    private string _featureSummary = string.Empty;

    protected override void OnExternalSettingsChanged()
    {
        _ = _diagnosticCoordinator.ApplyProtectionStateAsync(SettingsService.Current.IsEnabled);
        RefreshSafetyStatus();
        UpdateFeatureSummary();
    }

    protected override void OnSettingsLoaded()
    {
        RefreshSafetyStatus();
        UpdateFeatureSummary();
    }

    partial void OnIsProtectionEnabledChanged(bool value)
    {
        if (IsSyncing)
        {
            return;
        }

        PersistSetting(settings => settings.IsEnabled = value);
        _ = _diagnosticCoordinator.ApplyProtectionStateAsync(value);
        RefreshSafetyStatus();
        UpdateFeatureSummary();
    }

    partial void OnEmergencyPauseEnabledChanged(bool value)
    {
        if (IsSyncing)
        {
            return;
        }

        PersistSetting(settings => settings.EmergencyPauseEnabled = value);
        RefreshSafetyStatus();
        UpdateFeatureSummary();
    }

    partial void OnAutomaticLayoutEnabledChanged(bool value)
    {
        if (IsSyncing)
        {
            return;
        }

        PersistSetting(settings => settings.AutomaticLayoutEnabled = value);
        UpdateFeatureSummary();
    }

    partial void OnAutocorrectEnabledChanged(bool value)
    {
        if (IsSyncing)
        {
            return;
        }

        PersistSetting(settings => settings.AutocorrectEnabled = value);
        UpdateFeatureSummary();
    }

    partial void OnSnippetsEnabledChanged(bool value)
    {
        if (IsSyncing)
        {
            return;
        }

        PersistSetting(settings => settings.SnippetsEnabled = value);
        UpdateFeatureSummary();
    }

    [RelayCommand]
    private void RefreshSafetyStatus()
    {
        var summary = SafetyStatusFormatter.Create(
            SettingsService.Current,
            _automationSafetyService.EvaluateCurrentContext(),
            _diagnosticCoordinator.IsMonitoring);

        SafetyHeadline = summary.Headline;
        SafetyDetail = summary.Detail;
        SafetyLevelLabel = summary.Level switch
        {
            SafetyStatusLevel.Normal => "Обычно",
            SafetyStatusLevel.Caution => "Внимание",
            SafetyStatusLevel.Restricted => "Ограничено",
            SafetyStatusLevel.Inactive => "Неактивно",
            SafetyStatusLevel.Paused => "Пауза",
            _ => "Неизвестно",
        };
        IsMonitoringActive = summary.IsMonitoringActive;
    }

    protected override void SyncFromSettings(AppSettings settings)
    {
        IsProtectionEnabled = settings.IsEnabled;
        EmergencyPauseEnabled = settings.EmergencyPauseEnabled;
        AutomaticLayoutEnabled = settings.AutomaticLayoutEnabled;
        AutocorrectEnabled = settings.AutocorrectEnabled;
        SnippetsEnabled = settings.SnippetsEnabled;
    }

    private void UpdateFeatureSummary()
    {
        var settings = SettingsService.Current;
        var layout = settings.AutomaticLayoutEnabled ? "Автоисправление раскладки включено" : "Автоисправление раскладки выключено";
        var autocorrect = settings.AutocorrectEnabled ? "Автокоррекция включена" : "Автокоррекция выключена";
        var snippets = settings.SnippetsEnabled ? "Шаблоны включены" : "Шаблоны выключены";
        FeatureSummary = $"{layout} · {autocorrect} · {snippets} · Русский ↔ English";
    }
}
