using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Core.Validation;

namespace SmartInput.App.ViewModels;

public partial class ApplicationsViewModel : SettingsViewModelBase
{
    private readonly IAutomationSafetyService _automationSafetyService;

    public ApplicationsViewModel(
        ISettingsService settingsService,
        IAutomationSafetyService automationSafetyService)
        : base(settingsService)
    {
        _automationSafetyService = automationSafetyService;

        foreach (var processName in DefaultSafeModeRules.ProcessNames)
        {
            BuiltInSafeModeApplications.Add(processName);
        }
    }

    public ObservableCollection<string> BuiltInSafeModeApplications { get; } = [];

    [ObservableProperty]
    private string _excludedApplicationsText = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private bool _canSave;

    [ObservableProperty]
    private string _safeModeStatus = string.Empty;

    [ObservableProperty]
    private bool _startWithWindows;

    partial void OnExcludedApplicationsTextChanged(string value)
    {
        ValidateInput();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveExcludedApplications()
    {
        var validation = ExcludedApplicationNameValidator.ValidateText(ExcludedApplicationsText);
        if (!validation.IsValid)
        {
            ValidationMessage = string.Join(" ", validation.Errors);
            HasValidationErrors = true;
            CanSave = false;
            return;
        }

        _ = SettingsService.UpdateAsync(settings =>
        {
            settings.ExcludedApplications.Clear();
            settings.ExcludedApplications.AddRange(validation.NormalizedEntries);
        });

        ExcludedApplicationsText = string.Join(", ", validation.NormalizedEntries);
        ValidationMessage = validation.NormalizedEntries.Count == 0
            ? "Исключённые приложения не настроены."
            : $"Сохранено исключённых приложений: {validation.NormalizedEntries.Count}.";
        HasValidationErrors = false;
        RefreshSafeModeStatus();
    }

    [RelayCommand]
    private void RefreshSafeModeStatus()
    {
        var policy = _automationSafetyService.EvaluateCurrentContext();
        SafeModeStatus =
            $"Текущая политика: {policy.State}. Автоматизация: {(policy.AllowsAutomation ? "разрешена" : "заблокирована")}" +
            (policy.Reason is null ? string.Empty : $". {policy.Reason}");
    }

    protected override void OnSettingsLoaded()
    {
        ExcludedApplicationsText = string.Join(", ", SettingsService.Current.ExcludedApplications);
        StartWithWindows = SettingsService.Current.StartWithWindows;
        ValidateInput();
        RefreshSafeModeStatus();
    }

    protected override void SyncFromSettings(AppSettings settings)
    {
        StartWithWindows = settings.StartWithWindows;
    }

    private void ValidateInput()
    {
        var validation = ExcludedApplicationNameValidator.ValidateText(ExcludedApplicationsText);
        HasValidationErrors = !validation.IsValid;
        CanSave = validation.IsValid;

        if (validation.IsValid)
        {
            ValidationMessage = validation.NormalizedEntries.Count == 0
                ? "Введите имена процессов через запятую (например: bankapp, customtool)."
                : $"Допустимых приложений для сохранения: {validation.NormalizedEntries.Count}.";
        }
        else
        {
            ValidationMessage = string.Join(" ", validation.Errors);
        }

        SaveExcludedApplicationsCommand.NotifyCanExecuteChanged();
    }
}
