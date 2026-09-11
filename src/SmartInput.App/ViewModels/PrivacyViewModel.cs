using CommunityToolkit.Mvvm.ComponentModel;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.ViewModels;

public partial class PrivacyViewModel : SettingsViewModelBase
{
    public PrivacyViewModel(ISettingsService settingsService)
        : base(settingsService)
    {
    }

    [ObservableProperty]
    private string _privacyHeadline = "Только локальная обработка";

    [ObservableProperty]
    private string _dataCollectionSummary =
        "Smart Input не отправляет введённый текст, не ведёт историю ввода и не синхронизирует настройки с облаком.";

    [ObservableProperty]
    private string _diagnosticsSummary =
        "Расширенная диагностика показывает только коды клавиш и агрегированные статусы. Текст слов и метаданные окон никогда не записываются и не сохраняются.";

    [ObservableProperty]
    private string _learningSummary =
        "Обучение на отменах хранит только структурированную статистику вариантов и замен после успешной отмены. Шаблоны — это явные локальные настройки пользователя без истории ввода.";

    [ObservableProperty]
    private bool _isProtectionEnabled;

    protected override void SyncFromSettings(AppSettings settings)
    {
        IsProtectionEnabled = settings.IsEnabled;
        PrivacyHeadline = settings.IsEnabled
            ? "Защита включена — наблюдение ограничено логикой исправления"
            : "Защита выключена — наблюдение за клавиатурой неактивно";
    }
}
