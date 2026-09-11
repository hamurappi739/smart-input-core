using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.ViewModels;

/// <summary>
/// Local punctuation workbench. It never targets another application: the
/// user explicitly previews, accepts or reverts text inside this page.
/// </summary>
public partial class PunctuationPreviewViewModel : ViewModelBase
{
    private readonly IPunctuationPreviewService _service;
    private PunctuationPreviewResult? _lastPreview;

    public PunctuationPreviewViewModel(IPunctuationPreviewService service)
    {
        _service = service;
    }

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _status = "Введите фразу и нажмите «Проверить пунктуацию».";

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private bool _canApply;

    [RelayCommand]
    private void Analyze()
    {
        _lastPreview = _service.CreatePreview(SourceText);
        PreviewText = _lastPreview.PreviewText;
        HasPreview = _lastPreview.Status == PunctuationPreviewStatus.PreviewReady;
        CanApply = HasPreview;
        Status = _lastPreview.Status switch
        {
            PunctuationPreviewStatus.PreviewReady =>
                $"Найдено знаков: {_lastPreview.Edits.Count}. Проверьте результат перед применением.",
            PunctuationPreviewStatus.UnsafeInput => "Фраза содержит смешанный язык или небезопасные символы — оставлено без изменений.",
            PunctuationPreviewStatus.ExistingPunctuation => "В тексте уже есть знаки или технические символы — preview не выполнялся.",
            PunctuationPreviewStatus.InputTooLong => "Текст слишком длинный для локального preview (лимит 512 символов).",
            _ => "Надёжных предложений нет — текст оставлен без изменений.",
        };
    }

    [RelayCommand]
    private void ApplyPreview()
    {
        if (!CanApply || _lastPreview is null)
        {
            return;
        }

        SourceText = PreviewText;
        HasPreview = false;
        CanApply = false;
        Status = "Preview применён только в этом окне. Его можно отменить кнопкой «Отменить».";
    }

    [RelayCommand]
    private void UndoPreview()
    {
        if (_lastPreview is null)
        {
            return;
        }

        var original = _service.RevertPreview(_lastPreview);
        if (original is null)
        {
            Status = "Отменять нечего.";
            return;
        }

        SourceText = original;
        PreviewText = original;
        HasPreview = false;
        CanApply = false;
        Status = "Изменения отменены. Исходный текст восстановлен.";
    }

    [RelayCommand]
    private void Clear()
    {
        _lastPreview = null;
        SourceText = string.Empty;
        PreviewText = string.Empty;
        HasPreview = false;
        CanApply = false;
        Status = "Введите фразу и нажмите «Проверить пунктуацию».";
    }
}
