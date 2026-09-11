using CommunityToolkit.Mvvm.ComponentModel;
using SmartInput.Core.Models;

namespace SmartInput.App.ViewModels;

public partial class LanguagesViewModel : ViewModelBase
{
    public LanguagesViewModel()
    {
        ActiveLanguagesSummary = "Английский и русский используются для определения раскладки.";
    }

    [ObservableProperty]
    private bool _isEnglishActive = true;

    [ObservableProperty]
    private bool _isRussianActive = true;

    [ObservableProperty]
    private string _activeLanguagesSummary;

    [ObservableProperty]
    private string _availabilityNote =
        "Дополнительные языковые пакеты и отдельное включение языков пока недоступны. " +
        "Автоматическое исправление раскладки использует встроенную пару английский ↔ русский.";
}
