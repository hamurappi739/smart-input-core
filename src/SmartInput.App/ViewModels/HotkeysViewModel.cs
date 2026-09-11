using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.App.Services;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Hotkeys;
using System.Collections.ObjectModel;

namespace SmartInput.App.ViewModels;

public partial class HotkeysViewModel : SettingsViewModelBase, IDisposable
{
    private readonly IAutomaticLayoutHotkeyCoordinator _automaticLayoutHotkeyCoordinator;
    private readonly IUndoHotkeyCoordinator _undoHotkeyCoordinator;
    private readonly IManualCorrectionHotkeyCoordinator _manualCorrectionHotkeyCoordinator;

    public HotkeysViewModel(
        ISettingsService settingsService,
        IAutomaticLayoutHotkeyCoordinator automaticLayoutHotkeyCoordinator,
        IUndoHotkeyCoordinator undoHotkeyCoordinator,
        IManualCorrectionHotkeyCoordinator manualCorrectionHotkeyCoordinator)
        : base(settingsService)
    {
        _automaticLayoutHotkeyCoordinator = automaticLayoutHotkeyCoordinator;
        _undoHotkeyCoordinator = undoHotkeyCoordinator;
        _manualCorrectionHotkeyCoordinator = manualCorrectionHotkeyCoordinator;
        _automaticLayoutHotkeyCoordinator.RegistrationChanged += OnRegistrationChanged;
        _undoHotkeyCoordinator.RegistrationChanged += OnRegistrationChanged;
        _manualCorrectionHotkeyCoordinator.RegistrationChanged += OnRegistrationChanged;

        ManualCorrectionHotkeys = new ObservableCollection<ManualCorrectionHotkeyItemViewModel>(
            ManualCorrectionHotkeyCatalog.Definitions.Select(definition =>
                new ManualCorrectionHotkeyItemViewModel(definition.HotkeyId, definition.ActionLabel, definition.DefaultHotkey)));

        RefreshRegistrationStatus();
    }

    public string AvailabilityNote { get; } =
        "Быстрая отмена уже включена: дважды нажмите Shift сразу после исправления. Ниже можно настроить дополнительные сочетания Windows.";

    public ObservableCollection<ManualCorrectionHotkeyItemViewModel> ManualCorrectionHotkeys { get; }

    public IReadOnlyList<HotkeyPlaceholderItem> PlaceholderHotkeys { get; } =
    [
        new("Переключить экстренную паузу", "Пока недоступно"),
    ];

    [ObservableProperty]
    private string _automaticLayoutHotkeyText = HotkeyDefaults.ToggleAutomaticLayoutHotkey;

    [ObservableProperty]
    private string _automaticLayoutHotkeyStatus = "Регистрация ожидается.";

    [ObservableProperty]
    private string _automaticLayoutHotkeyValidationMessage = string.Empty;

    [ObservableProperty]
    private bool _isApplyingAutomaticLayoutHotkey;

    [ObservableProperty]
    private string _undoHotkeyText = HotkeyDefaults.UndoLastCorrectionHotkey;

    [ObservableProperty]
    private string _undoHotkeyStatus = "Регистрация ожидается.";

    [ObservableProperty]
    private string _undoHotkeyValidationMessage = string.Empty;

    [ObservableProperty]
    private bool _isApplyingUndoHotkey;

    [RelayCommand]
    private async Task ApplyAutomaticLayoutHotkeyAsync()
    {
        if (IsApplyingAutomaticLayoutHotkey)
        {
            return;
        }

        IsApplyingAutomaticLayoutHotkey = true;
        AutomaticLayoutHotkeyValidationMessage = string.Empty;

        try
        {
            var parsed = HotkeyBindingParser.Parse(AutomaticLayoutHotkeyText);
            if (parsed.ValidationState is not HotkeyValidationState.Empty && !parsed.IsValid)
            {
                AutomaticLayoutHotkeyValidationMessage = parsed.ValidationMessage ?? "Недопустимая горячая клавиша.";
                AutomaticLayoutHotkeyStatus = HotkeyStatusFormatter.Format(parsed.ValidationState);
                return;
            }

            AutomaticLayoutHotkeyText = parsed.DisplayName;
            var result = await _automaticLayoutHotkeyCoordinator
                .ApplyBindingAsync(parsed.DisplayName)
                .ConfigureAwait(true);
            UpdateAutomaticLayoutStatusFromResult(result);
        }
        catch (Exception)
        {
            AutomaticLayoutHotkeyStatus = "Не удалось применить горячую клавишу.";
            AutomaticLayoutHotkeyValidationMessage = "Неожиданная ошибка при регистрации горячей клавиши.";
        }
        finally
        {
            IsApplyingAutomaticLayoutHotkey = false;
        }
    }

    [RelayCommand]
    private void ResetAutomaticLayoutHotkey()
    {
        AutomaticLayoutHotkeyText = HotkeyDefaults.ToggleAutomaticLayoutHotkey;
        _ = ApplyAutomaticLayoutHotkeyAsync();
    }

    [RelayCommand]
    private async Task ApplyUndoHotkeyAsync()
    {
        if (IsApplyingUndoHotkey)
        {
            return;
        }

        IsApplyingUndoHotkey = true;
        UndoHotkeyValidationMessage = string.Empty;

        try
        {
            var parsed = HotkeyBindingParser.Parse(UndoHotkeyText);
            if (!parsed.IsValid)
            {
                UndoHotkeyValidationMessage = parsed.ValidationMessage ?? "Недопустимая горячая клавиша.";
                UndoHotkeyStatus = HotkeyStatusFormatter.Format(parsed.ValidationState);
                return;
            }

            UndoHotkeyText = parsed.DisplayName;
            var result = await _undoHotkeyCoordinator
                .ApplyBindingAsync(parsed.DisplayName)
                .ConfigureAwait(true);

            UpdateUndoStatusFromResult(result);
        }
        catch (Exception)
        {
            UndoHotkeyStatus = "Не удалось применить горячую клавишу.";
            UndoHotkeyValidationMessage = "Неожиданная ошибка при регистрации горячей клавиши.";
        }
        finally
        {
            IsApplyingUndoHotkey = false;
        }
    }

    [RelayCommand]
    private void ResetUndoHotkey()
    {
        UndoHotkeyText = HotkeyDefaults.UndoLastCorrectionHotkey;
        _ = ApplyUndoHotkeyAsync();
    }

    [RelayCommand]
    private async Task ApplyManualHotkeyAsync(ManualCorrectionHotkeyItemViewModel? item)
    {
        if (item is null || item.IsApplying)
        {
            return;
        }

        item.IsApplying = true;
        item.ValidationMessage = string.Empty;

        try
        {
            var parsed = HotkeyBindingParser.Parse(item.HotkeyText);
            if (parsed.ValidationState is HotkeyValidationState.Empty)
            {
                var unassigned = await _manualCorrectionHotkeyCoordinator
                    .ApplyBindingAsync(item.HotkeyId, string.Empty)
                    .ConfigureAwait(true);

                item.HotkeyText = string.Empty;
                UpdateManualItemStatus(item, unassigned);
                return;
            }

            if (!parsed.IsValid)
            {
                item.ValidationMessage = parsed.ValidationMessage ?? "Недопустимая горячая клавиша.";
                item.Status = HotkeyStatusFormatter.Format(parsed.ValidationState);
                return;
            }

            item.HotkeyText = parsed.DisplayName;
            var result = await _manualCorrectionHotkeyCoordinator
                .ApplyBindingAsync(item.HotkeyId, parsed.DisplayName)
                .ConfigureAwait(true);

            UpdateManualItemStatus(item, result);
        }
        catch (Exception)
        {
            item.Status = "Не удалось применить горячую клавишу.";
            item.ValidationMessage = "Неожиданная ошибка при регистрации горячей клавиши.";
        }
        finally
        {
            item.IsApplying = false;
        }
    }

    [RelayCommand]
    private void ResetManualHotkey(ManualCorrectionHotkeyItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.HotkeyText = item.DefaultHotkey;
        _ = ApplyManualHotkeyAsync(item);
    }

    [RelayCommand]
    private void RefreshRegistrationStatus()
    {
        AutomaticLayoutHotkeyText = _automaticLayoutHotkeyCoordinator.CurrentBindingDisplayName;
        UpdateAutomaticLayoutStatusFromResult(_automaticLayoutHotkeyCoordinator.RegistrationStatus);

        UndoHotkeyText = _undoHotkeyCoordinator.CurrentBindingDisplayName;
        UpdateUndoStatusFromResult(_undoHotkeyCoordinator.RegistrationStatus);

        foreach (var item in ManualCorrectionHotkeys)
        {
            if (_manualCorrectionHotkeyCoordinator.CurrentBindingDisplayNames.TryGetValue(item.HotkeyId, out var displayName))
            {
                item.HotkeyText = displayName;
            }

            _manualCorrectionHotkeyCoordinator.RegistrationStatuses.TryGetValue(item.HotkeyId, out var status);
            UpdateManualItemStatus(item, status);
        }
    }

    public void Dispose()
    {
        _automaticLayoutHotkeyCoordinator.RegistrationChanged -= OnRegistrationChanged;
        _undoHotkeyCoordinator.RegistrationChanged -= OnRegistrationChanged;
        _manualCorrectionHotkeyCoordinator.RegistrationChanged -= OnRegistrationChanged;
    }

    protected override void SyncFromSettings(AppSettings settings)
    {
        AutomaticLayoutHotkeyText = string.IsNullOrWhiteSpace(settings.ToggleAutomaticLayoutHotkey)
            ? string.Empty
            : settings.ToggleAutomaticLayoutHotkey;

        UndoHotkeyText = string.IsNullOrWhiteSpace(settings.UndoLastCorrectionHotkey)
            ? HotkeyDefaults.UndoLastCorrectionHotkey
            : settings.UndoLastCorrectionHotkey;

        foreach (var item in ManualCorrectionHotkeys)
        {
            item.HotkeyText = ManualCorrectionHotkeyCatalog.GetConfiguredHotkey(settings, item.HotkeyId);
        }
    }

    protected override void OnSettingsLoaded()
    {
        RefreshRegistrationStatus();
    }

    private void OnRegistrationChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshRegistrationStatus);
    }

    private void UpdateUndoStatusFromResult(GlobalHotkeyRegistrationResult? result)
    {
        UndoHotkeyStatus = HotkeyStatusFormatter.Format(result);
        UndoHotkeyValidationMessage = HotkeyStatusFormatter.ValidationMessage(result);
    }

    private void UpdateAutomaticLayoutStatusFromResult(GlobalHotkeyRegistrationResult? result)
    {
        AutomaticLayoutHotkeyStatus = HotkeyStatusFormatter.Format(result);
        AutomaticLayoutHotkeyValidationMessage = HotkeyStatusFormatter.ValidationMessage(result);
    }

    private static void UpdateManualItemStatus(
        ManualCorrectionHotkeyItemViewModel item,
        GlobalHotkeyRegistrationResult? result)
    {
        item.Status = HotkeyStatusFormatter.Format(result);
        item.ValidationMessage = HotkeyStatusFormatter.ValidationMessage(result);
    }
}

public partial class ManualCorrectionHotkeyItemViewModel : ObservableObject
{
    public ManualCorrectionHotkeyItemViewModel(string hotkeyId, string actionLabel, string defaultHotkey)
    {
        HotkeyId = hotkeyId;
        ActionLabel = actionLabel;
        DefaultHotkey = defaultHotkey;
        _hotkeyText = defaultHotkey;
        _status = "Регистрация ожидается.";
    }

    public string HotkeyId { get; }

    public string ActionLabel { get; }

    public string DefaultHotkey { get; }

    [ObservableProperty]
    private string _hotkeyText;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _isApplying;
}

public sealed class HotkeyPlaceholderItem(string action, string status)
{
    public string Action { get; } = action;

    public string Status { get; } = status;
}

internal static class HotkeyStatusFormatter
{
    public static string Format(GlobalHotkeyRegistrationResult? result)
    {
        if (result is null)
        {
            return "Не зарегистрирована.";
        }

        return result.State switch
        {
            GlobalHotkeyRegistrationState.Registered =>
                $"Зарегистрирована: {result.DisplayName}",
            GlobalHotkeyRegistrationState.Conflict =>
                $"Конфликт: {result.ErrorMessage}",
            GlobalHotkeyRegistrationState.InvalidBinding =>
                $"Недопустимо: {result.ErrorMessage}",
            GlobalHotkeyRegistrationState.Failed =>
                $"Ошибка: {result.ErrorMessage}",
            GlobalHotkeyRegistrationState.NotRegistered =>
                "Отключена / не назначена.",
            _ => "Не зарегистрирована.",
        };
    }

    public static string Format(HotkeyValidationState validationState)
    {
        return validationState switch
        {
            HotkeyValidationState.Empty => "Отключена / не назначена.",
            _ => $"Недопустимо: {validationState}",
        };
    }

    public static string ValidationMessage(GlobalHotkeyRegistrationResult? result)
    {
        if (result is null)
        {
            return string.Empty;
        }

        return result.State is GlobalHotkeyRegistrationState.Conflict
            or GlobalHotkeyRegistrationState.Failed
            or GlobalHotkeyRegistrationState.InvalidBinding
            ? result.ErrorMessage ?? string.Empty
            : string.Empty;
    }
}
