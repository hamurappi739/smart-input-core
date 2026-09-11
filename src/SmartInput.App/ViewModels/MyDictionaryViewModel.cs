using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;

namespace SmartInput.App.ViewModels;

public partial class MyDictionaryViewModel : ViewModelBase
{
    private readonly IUserAutocorrectDictionaryStore _dictionaryStore;
    private readonly ICorrectionRejectionLearningStore _rejectionStore;
    private readonly ICorrectionRejectionPolicy _rejectionPolicy;

    public MyDictionaryViewModel(
        IUserAutocorrectDictionaryStore dictionaryStore,
        ICorrectionRejectionLearningStore rejectionStore,
        ICorrectionRejectionPolicy rejectionPolicy)
    {
        _dictionaryStore = dictionaryStore;
        _rejectionStore = rejectionStore;
        _rejectionPolicy = rejectionPolicy;
        _ = InitializeAsync();
    }

    public string PageNote { get; } =
        "Добавьте свои слова и управляйте отклонёнными исправлениями. Словарь хранится только на этом компьютере.";

    public IReadOnlyList<string> LanguageOptions { get; } =
    [
        "Русский",
        "Английский",
    ];

    public ObservableCollection<DictionaryWordItemViewModel> Words { get; } = [];

    public ObservableCollection<RejectedCorrectionItemViewModel> RejectedCorrections { get; } = [];

    [ObservableProperty]
    private bool _hasWords;

    [ObservableProperty]
    private bool _hasRejectedCorrections;

    public string WordsCountLabel => Words.Count switch
    {
        0 => "Добавленных слов пока нет",
        1 => "Добавлено слов: 1",
        _ => $"Добавлено слов: {Words.Count}",
    };

    public string RejectedCorrectionsCountLabel => RejectedCorrections.Count switch
    {
        0 => "Отклонённых исправлений пока нет",
        1 => "Отклонённых исправлений: 1",
        _ => $"Отклонённых исправлений: {RejectedCorrections.Count}",
    };

    [ObservableProperty]
    private string _newWord = string.Empty;

    [ObservableProperty]
    private string _selectedLanguageOption = "Русский";

    [ObservableProperty]
    private bool _neverAutocorrect;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task AddWordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ValidationMessage = string.Empty;
        HasValidationErrors = false;

        var trimmed = NewWord.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            ValidationMessage = "Введите слово или название.";
            HasValidationErrors = true;
            return;
        }

        IsBusy = true;
        try
        {
            await _dictionaryStore.AddOrUpdateAsync(new UserAutocorrectDictionaryEntry
            {
                Word = trimmed,
                Language = MapLanguage(SelectedLanguageOption),
                NeverAutocorrect = NeverAutocorrect,
            }).ConfigureAwait(true);

            await _dictionaryStore.SaveAsync().ConfigureAwait(true);
            NewWord = string.Empty;
            NeverAutocorrect = false;
            await ReloadAsync().ConfigureAwait(true);
            StatusMessage = "Слово сохранено. Оно действует только локально.";
        }
        catch (ArgumentException)
        {
            HasValidationErrors = true;
            ValidationMessage = "Не удалось сохранить запись. Проверьте слово и выбранный язык.";
        }
        catch (InvalidOperationException)
        {
            HasValidationErrors = true;
            ValidationMessage = "Личный словарь заполнен. Удалите ненужную запись и попробуйте снова.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveWordAsync(DictionaryWordItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _dictionaryStore
                .RemoveAsync(item.Word, item.Language)
                .ConfigureAwait(true);
            await _dictionaryStore.SaveAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            StatusMessage = "Слово удалено из личного словаря.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AllowAgainAsync(RejectedCorrectionItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _rejectionPolicy
                .AllowAgainAsync(item.Candidate, item.Replacement, item.Kind)
                .ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            StatusMessage = "Исправление снова разрешено.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await _dictionaryStore.LoadAsync().ConfigureAwait(true);
            await _rejectionStore.EnsureLoadedAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            StatusMessage = Words.Count == 0 && RejectedCorrections.Count == 0
                ? "Словарь пока пуст."
                : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync()
    {
        Words.Clear();
        foreach (var entry in _dictionaryStore.Entries)
        {
            Words.Add(new DictionaryWordItemViewModel(entry));
        }
        HasWords = Words.Count > 0;
        OnPropertyChanged(nameof(WordsCountLabel));

        RejectedCorrections.Clear();
        var rejections = await _rejectionStore.GetEntriesAsync().ConfigureAwait(true);
        foreach (var entry in rejections.Where(static e => e.UndoCount >= CorrectionRejectionPolicy.DefaultSuppressionThreshold))
        {
            RejectedCorrections.Add(new RejectedCorrectionItemViewModel(entry));
        }
        HasRejectedCorrections = RejectedCorrections.Count > 0;
        OnPropertyChanged(nameof(RejectedCorrectionsCountLabel));
    }

    private static TypingLanguage MapLanguage(string option)
    {
        return option == "Английский" ? TypingLanguage.English : TypingLanguage.Russian;
    }
}

public sealed class DictionaryWordItemViewModel
{
    public DictionaryWordItemViewModel(UserAutocorrectDictionaryEntry entry)
    {
        Word = entry.Word;
        Language = entry.Language;
        LanguageLabel = entry.Language == TypingLanguage.English ? "Английский" : "Русский";
        NeverAutocorrectLabel = entry.NeverAutocorrect ? "Не исправлять" : string.Empty;
        NeverAutocorrect = entry.NeverAutocorrect;
    }

    public string Word { get; }

    public TypingLanguage Language { get; }

    public string LanguageLabel { get; }

    public string NeverAutocorrectLabel { get; }

    public bool NeverAutocorrect { get; }
}

public sealed class RejectedCorrectionItemViewModel
{
    public RejectedCorrectionItemViewModel(CorrectionRejectionLearningEntry entry)
    {
        Candidate = entry.Candidate;
        Replacement = entry.Replacement;
        Kind = entry.Kind;
        KindLabel = entry.Kind switch
        {
            CorrectionKind.Layout => "Раскладка",
            CorrectionKind.Autocorrect => "Опечатка",
            CorrectionKind.Combined => "Опечатка",
            CorrectionKind.Snippet => "Шаблон",
            CorrectionKind.Punctuation => "Пунктуация",
            _ => "Исправление",
        };
        Summary = $"{KindLabel}: {entry.Candidate} → {entry.Replacement}";
    }

    public string Candidate { get; }

    public string Replacement { get; }

    public CorrectionKind Kind { get; }

    public string KindLabel { get; }

    public string Summary { get; }
}
