using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Core.Validation;

namespace SmartInput.App.ViewModels;

public partial class SnippetsViewModel : SettingsViewModelBase
{
    private readonly ISnippetService _snippetService;
    private Guid? _editingId;
    private bool _isSyncingSelection;

    public SnippetsViewModel(ISettingsService settingsService, ISnippetService snippetService)
        : base(settingsService)
    {
        _snippetService = snippetService;
        _ = InitializeSnippetsAsync();
    }

    public string PageNote { get; } =
        "Шаблоны — это явные локальные настройки пользователя. Включите автоматическую подстановку ниже; подстановки не отправляются наружу и не запускают другие шаблоны.";

    [ObservableProperty]
    private bool _snippetsEnabled;

    public IReadOnlyList<string> LanguageOptions { get; } =
    [
        "Любой",
        "Английский",
        "Русский",
    ];

    public ObservableCollection<SnippetListItemViewModel> Snippets { get; } = [];

    [ObservableProperty]
    private bool _hasSnippets;

    public string SnippetsCountLabel => Snippets.Count switch
    {
        0 => "Шаблонов пока нет",
        1 => "Сохранён шаблон: 1",
        _ => $"Сохранено шаблонов: {Snippets.Count}",
    };

    [ObservableProperty]
    private SnippetListItemViewModel? _selectedSnippet;

    [ObservableProperty]
    private string _editorTitle = "Добавить шаблон";

    [ObservableProperty]
    private string _triggerText = string.Empty;

    [ObservableProperty]
    private string _replacementText = string.Empty;

    [ObservableProperty]
    private string _selectedLanguageOption = "Любой";

    [ObservableProperty]
    private bool _caseSensitive;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _enabledApplicationsText = string.Empty;

    [ObservableProperty]
    private string _disabledApplicationsText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Шаблоны не настроены.";

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSnippetsEnabledChanged(bool value)
    {
        PersistSetting(settings => settings.SnippetsEnabled = value);
    }

    protected override void SyncFromSettings(AppSettings settings)
    {
        SnippetsEnabled = settings.SnippetsEnabled;
    }

    partial void OnSelectedSnippetChanged(SnippetListItemViewModel? value)
    {
        if (_isSyncingSelection)
        {
            return;
        }

        if (value is null)
        {
            return;
        }

        LoadEditorFromSnippet(value.Definition);
    }

    [RelayCommand]
    private void NewSnippet()
    {
        ClearEditor();
        StatusMessage = "Создание нового локального шаблона.";
    }

    [RelayCommand]
    private async Task SaveSnippetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var wasAdd = _editingId is null;
            var definition = BuildDefinitionFromEditor();
            var result = wasAdd
                ? await _snippetService.AddAsync(definition).ConfigureAwait(true)
                : await _snippetService.UpdateAsync(definition).ConfigureAwait(true);

            if (!result.IsValid)
            {
                HasValidationErrors = true;
                ValidationMessage = string.Join(" ", result.Errors);
                StatusMessage = "Проверьте поля и попробуйте снова.";
                return;
            }

            HasValidationErrors = false;
            ValidationMessage = string.Empty;
            await ReloadSnippetsAsync().ConfigureAwait(true);

            var saved = result.Normalized!;
            SelectSnippetById(saved.Id);
            StatusMessage = wasAdd
                ? "Шаблон добавлен."
                : "Шаблон обновлён.";
            _editingId = saved.Id;
            EditorTitle = "Изменить шаблон";
        }
        catch (Exception)
        {
            HasValidationErrors = true;
            ValidationMessage = "Не удалось сохранить шаблон. Проверьте данные и повторите попытку.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSnippetAsync()
    {
        if (IsBusy || _editingId is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _snippetService.DeleteAsync(_editingId.Value).ConfigureAwait(true);
            ClearEditor();
            await ReloadSnippetsAsync().ConfigureAwait(true);
            StatusMessage = "Шаблон удалён.";
        }
        catch (Exception)
        {
            StatusMessage = "Не удалось удалить шаблон. Попробуйте ещё раз.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(SnippetListItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var next = !item.IsEnabled;
            await _snippetService.SetEnabledAsync(item.Id, next).ConfigureAwait(true);
            await ReloadSnippetsAsync().ConfigureAwait(true);
            SelectSnippetById(item.Id);
            StatusMessage = next ? "Шаблон включён." : "Шаблон выключен.";
        }
        catch (Exception)
        {
            StatusMessage = "Не удалось изменить состояние шаблона.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InitializeSnippetsAsync()
    {
        try
        {
            await _snippetService.LoadAsync().ConfigureAwait(true);
            await ReloadSnippetsAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            StatusMessage = "Не удалось загрузить шаблоны. Проверьте локальное хранилище.";
        }
    }

    private Task ReloadSnippetsAsync()
    {
        var selectedId = SelectedSnippet?.Id ?? _editingId;
        Snippets.Clear();
        foreach (var snippet in _snippetService.Snippets)
        {
            Snippets.Add(new SnippetListItemViewModel(snippet));
        }

        HasSnippets = Snippets.Count > 0;
        OnPropertyChanged(nameof(SnippetsCountLabel));

        StatusMessage = Snippets.Count == 0
            ? "Шаблоны не настроены."
            : $"Локальных шаблонов: {Snippets.Count}. Для автоматической подстановки нужны включённые защита и шаблоны текста.";

        if (selectedId is Guid id)
        {
            SelectSnippetById(id);
        }

        return Task.CompletedTask;
    }

    private void SelectSnippetById(Guid id)
    {
        var match = Snippets.FirstOrDefault(item => item.Id == id);
        _isSyncingSelection = true;
        SelectedSnippet = match;
        _isSyncingSelection = false;
        if (match is not null)
        {
            LoadEditorFromSnippet(match.Definition);
        }
    }

    private void LoadEditorFromSnippet(SnippetDefinition snippet)
    {
        _editingId = snippet.Id;
        EditorTitle = "Изменить шаблон";
        TriggerText = snippet.Trigger;
        ReplacementText = snippet.Replacement;
        SelectedLanguageOption = snippet.Language?.ToString() ?? "Любой";
        CaseSensitive = snippet.CaseSensitive;
        IsEnabled = snippet.IsEnabled;
        EnabledApplicationsText = string.Join(", ", snippet.EnabledApplications);
        DisabledApplicationsText = string.Join(", ", snippet.DisabledApplications);
        HasValidationErrors = false;
        ValidationMessage = string.Empty;
    }

    private void ClearEditor()
    {
        _editingId = null;
        EditorTitle = "Добавить шаблон";
        TriggerText = string.Empty;
        ReplacementText = string.Empty;
        SelectedLanguageOption = "Любой";
        CaseSensitive = false;
        IsEnabled = true;
        EnabledApplicationsText = string.Empty;
        DisabledApplicationsText = string.Empty;
        HasValidationErrors = false;
        ValidationMessage = string.Empty;

        _isSyncingSelection = true;
        SelectedSnippet = null;
        _isSyncingSelection = false;
    }

    private SnippetDefinition BuildDefinitionFromEditor()
    {
        TypingLanguage? language = SelectedLanguageOption switch
        {
            "Английский" => TypingLanguage.English,
            "Русский" => TypingLanguage.Russian,
            _ => null,
        };

        return new SnippetDefinition
        {
            Id = _editingId ?? Guid.Empty,
            Trigger = TriggerText,
            Replacement = ReplacementText,
            Language = language,
            CaseSensitive = CaseSensitive,
            IsEnabled = IsEnabled,
            EnabledApplications = ExcludedApplicationNameValidator.ParseEntries(EnabledApplicationsText),
            DisabledApplications = ExcludedApplicationNameValidator.ParseEntries(DisabledApplicationsText),
        };
    }
}

public sealed class SnippetListItemViewModel(SnippetDefinition definition)
{
    public SnippetDefinition Definition { get; } = definition;

    public Guid Id => Definition.Id;

    public string Trigger => Definition.Trigger;

    public string ReplacementPreview =>
        Definition.Replacement.Length <= 48
            ? Definition.Replacement.Replace("\r\n", "\\n", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
            : Definition.Replacement[..45]
                .Replace("\r\n", "\\n", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal) + "...";

    public string LanguageLabel => Definition.Language switch
    {
        TypingLanguage.English => "Английский",
        TypingLanguage.Russian => "Русский",
        _ => "Любой",
    };

    public bool IsEnabled => Definition.IsEnabled;

    public string EnabledLabel => Definition.IsEnabled ? "Включён" : "Выключен";

    public string ToggleLabel => Definition.IsEnabled ? "Выключить" : "Включить";
}
