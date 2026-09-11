using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.App.Services;

public enum PredictionPreviewDisplayState
{
    Empty,
    NoSuggestion,
    LowConfidence,
    Suggestion,
    Disabled,
    PolicyBlocked,
}

public sealed class PredictionPreviewEvaluation
{
    public PredictionPreviewDisplayState State { get; init; }

    public string? GhostSuffix { get; init; }

    public double Confidence { get; init; }

    public PredictionRecommendation Recommendation { get; init; }

    public required string StatusMessage { get; init; }

    public bool CanAccept => State is PredictionPreviewDisplayState.Suggestion
        or PredictionPreviewDisplayState.LowConfidence;

    public bool HasGhost => !string.IsNullOrEmpty(GhostSuffix);
}

public interface IPredictionPreviewService
{
    PredictionPreviewEvaluation Evaluate(
        string previewText,
        int caretIndex,
        TypingLanguage activeLanguage,
        bool suggestionDismissed);

    string AcceptSuggestion(string previewText, int caretIndex, string ghostSuffix);

    int GetCaretIndexAfterAccept(int caretIndex, string ghostSuffix);
}

public sealed class PredictionPreviewService : IPredictionPreviewService
{
    private readonly IPredictionService _predictionService;
    private readonly ISettingsService _settingsService;
    private readonly IAutomationSafetyService _automationSafetyService;

    public PredictionPreviewService(
        IPredictionService predictionService,
        ISettingsService settingsService,
        IAutomationSafetyService automationSafetyService)
    {
        _predictionService = predictionService;
        _settingsService = settingsService;
        _automationSafetyService = automationSafetyService;
    }

    public PredictionPreviewEvaluation Evaluate(
        string previewText,
        int caretIndex,
        TypingLanguage activeLanguage,
        bool suggestionDismissed)
    {
        previewText ??= string.Empty;
        caretIndex = ClampCaretIndex(previewText, caretIndex);

        if (string.IsNullOrWhiteSpace(previewText))
        {
            return CreateState(
                PredictionPreviewDisplayState.Empty,
                "Только предпросмотр — вводите текст в поле ниже, чтобы проверить локальные подсказки.");
        }

        if (!_settingsService.Current.PredictionEnabled)
        {
            return CreateState(
                PredictionPreviewDisplayState.Disabled,
                "Только предпросмотр — подсказки выключены в настройках исправлений.");
        }

        var policy = _automationSafetyService.EvaluateCurrentContext();
        if (!IsPolicyAllowedForPreview(policy))
        {
            return CreateState(
                PredictionPreviewDisplayState.PolicyBlocked,
                $"Только предпросмотр — подсказки недоступны при текущем состоянии политики: {policy.State}.");
        }

        if (suggestionDismissed)
        {
            return CreateState(
                PredictionPreviewDisplayState.NoSuggestion,
                "Только предпросмотр — подсказка скрыта. Продолжайте ввод, чтобы обновить её.");
        }

        var context = previewText[..caretIndex];
        var prefix = ExtractCurrentWordPrefix(context);
        var result = _predictionService.Predict(new PredictionRequest
        {
            Context = context,
            ActiveLanguage = activeLanguage,
            CurrentWordPrefix = prefix,
        });

        return MapPredictionResult(result);
    }

    public string AcceptSuggestion(string previewText, int caretIndex, string ghostSuffix)
    {
        previewText ??= string.Empty;
        caretIndex = ClampCaretIndex(previewText, caretIndex);

        if (string.IsNullOrEmpty(ghostSuffix))
        {
            return previewText;
        }

        return previewText.Insert(caretIndex, ghostSuffix);
    }

    public int GetCaretIndexAfterAccept(int caretIndex, string ghostSuffix)
    {
        return caretIndex + (ghostSuffix?.Length ?? 0);
    }

    private PredictionPreviewEvaluation MapPredictionResult(PredictionResult result)
    {
        if (result.Recommendation == PredictionRecommendation.NoSuggestion
            || string.IsNullOrEmpty(result.SuggestedContinuation))
        {
            return CreateState(
                PredictionPreviewDisplayState.NoSuggestion,
                "Только предпросмотр — для текущего контекста нет подсказки.",
                recommendation: result.Recommendation,
                confidence: result.Confidence);
        }

        if (result.Recommendation == PredictionRecommendation.LowConfidence)
        {
            return CreateState(
                PredictionPreviewDisplayState.LowConfidence,
                $"Только предпросмотр — доступна подсказка с низкой уверенностью ({result.Confidence:F2}). Нажмите Tab, чтобы принять, или Esc, чтобы скрыть.",
                ghostSuffix: result.SuggestedContinuation,
                recommendation: result.Recommendation,
                confidence: result.Confidence);
        }

        return CreateState(
            PredictionPreviewDisplayState.Suggestion,
            $"Только предпросмотр — доступна подсказка (уверенность {result.Confidence:F2}). Нажмите Tab, чтобы принять, или Esc, чтобы скрыть.",
            ghostSuffix: result.SuggestedContinuation,
            recommendation: result.Recommendation,
            confidence: result.Confidence);
    }

    private static PredictionPreviewEvaluation CreateState(
        PredictionPreviewDisplayState state,
        string statusMessage,
        string? ghostSuffix = null,
        PredictionRecommendation recommendation = PredictionRecommendation.NoSuggestion,
        double confidence = 0.0)
    {
        return new PredictionPreviewEvaluation
        {
            State = state,
            GhostSuffix = ghostSuffix,
            Confidence = confidence,
            Recommendation = recommendation,
            StatusMessage = statusMessage,
        };
    }

    private static bool IsPolicyAllowedForPreview(AutomationPolicyResult policy)
    {
        return policy.State == AutomationPolicyState.Allowed
            && policy.AllowsAutomation
            && !policy.IsEmergencyPaused;
    }

    private static int ClampCaretIndex(string text, int caretIndex)
    {
        if (caretIndex < 0)
        {
            return 0;
        }

        return caretIndex > text.Length ? text.Length : caretIndex;
    }

    private static string? ExtractCurrentWordPrefix(string context)
    {
        if (string.IsNullOrEmpty(context) || char.IsWhiteSpace(context[^1]))
        {
            return null;
        }

        var end = context.Length - 1;
        while (end >= 0 && char.IsLetter(context[end]))
        {
            end--;
        }

        var prefix = context[(end + 1)..];
        return string.IsNullOrEmpty(prefix) ? null : prefix;
    }
}
