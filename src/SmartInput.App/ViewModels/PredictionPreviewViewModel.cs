using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartInput.App.Services;
using SmartInput.Core.Models;

namespace SmartInput.App.ViewModels;

public partial class PredictionPreviewViewModel : ViewModelBase
{
    private readonly IPredictionPreviewService _predictionPreviewService;
    private bool _isDismissed;
    private bool _isApplyingSuggestion;
    private bool _isSyncingCaret;

    public PredictionPreviewViewModel(IPredictionPreviewService predictionPreviewService)
    {
        _predictionPreviewService = predictionPreviewService;
        RefreshPrediction();
    }

    public string PreviewOnlyLabel { get; } = "Только предпросмотр";

    public IReadOnlyList<string> ActiveLanguageOptions { get; } =
    [
        "Английский",
        "Русский",
    ];

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private int _caretIndex;

    [ObservableProperty]
    private string _selectedLanguageOption = "Английский";

    [ObservableProperty]
    private string _ghostSuffix = string.Empty;

    [ObservableProperty]
    private string _previewStatus = "Только предпросмотр — вводите текст в поле ниже, чтобы проверить локальные подсказки.";

    [ObservableProperty]
    private bool _hasGhostSuggestion;

    [ObservableProperty]
    private bool _canAcceptSuggestion;

    [ObservableProperty]
    private PredictionPreviewDisplayState _displayState = PredictionPreviewDisplayState.Empty;

    partial void OnPreviewTextChanged(string value)
    {
        if (_isApplyingSuggestion)
        {
            return;
        }

        _isDismissed = false;
        _isSyncingCaret = true;
        CaretIndex = value.Length;
        _isSyncingCaret = false;
        RefreshPrediction();
    }

    partial void OnCaretIndexChanged(int value)
    {
        if (_isSyncingCaret || _isApplyingSuggestion)
        {
            return;
        }

        RefreshPrediction();
    }

    partial void OnSelectedLanguageOptionChanged(string value)
    {
        _isDismissed = false;
        RefreshPrediction();
    }

    public void UpdateCaretIndex(int caretIndex)
    {
        CaretIndex = caretIndex;
    }

    [RelayCommand]
    public void AcceptSuggestion()
    {
        if (!CanAcceptSuggestion || string.IsNullOrEmpty(GhostSuffix))
        {
            return;
        }

        _isApplyingSuggestion = true;
        try
        {
            PreviewText = _predictionPreviewService.AcceptSuggestion(PreviewText, CaretIndex, GhostSuffix);
            CaretIndex = _predictionPreviewService.GetCaretIndexAfterAccept(CaretIndex, GhostSuffix);
            _isDismissed = false;
            ClearGhost();
            RefreshPrediction();
        }
        finally
        {
            _isApplyingSuggestion = false;
        }
    }

    [RelayCommand]
    public void DismissSuggestion()
    {
        if (!HasGhostSuggestion)
        {
            return;
        }

        _isDismissed = true;
        ClearGhost();
        PreviewStatus = "Только предпросмотр — подсказка скрыта. Продолжайте ввод, чтобы обновить её.";
        DisplayState = PredictionPreviewDisplayState.NoSuggestion;
    }

    [RelayCommand]
    private void RefreshPreview()
    {
        RefreshPrediction();
    }

    private void RefreshPrediction()
    {
        var evaluation = _predictionPreviewService.Evaluate(
            PreviewText,
            CaretIndex,
            ResolveActiveLanguage(),
            _isDismissed);

        DisplayState = evaluation.State;
        PreviewStatus = evaluation.StatusMessage;
        CanAcceptSuggestion = evaluation.CanAccept;

        if (evaluation.HasGhost)
        {
            GhostSuffix = evaluation.GhostSuffix!;
            HasGhostSuggestion = true;
        }
        else
        {
            ClearGhost();
        }
    }

    private void ClearGhost()
    {
        GhostSuffix = string.Empty;
        HasGhostSuggestion = false;
        CanAcceptSuggestion = false;
    }

    private TypingLanguage ResolveActiveLanguage()
    {
        return SelectedLanguageOption switch
        {
            "Русский" => TypingLanguage.Russian,
            _ => TypingLanguage.English,
        };
    }
}
