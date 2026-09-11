namespace SmartInput.Core.Models;

public enum LivePredictionAction
{
    None,
    BufferReset,
    PredictionUpdated,
    PredictionCleared,
    SkippedDisabled,
    SkippedPolicy,
    SuggestionAccepted,
    SuggestionDismissed,
}

public sealed class LivePredictionStatus
{
    public bool HasSuggestion { get; init; }

    public double Confidence { get; init; }

    public PredictionRecommendation Recommendation { get; init; }

    public int SuggestionTokenCount { get; init; }

    public int SuggestionCharacterCount { get; init; }

    public int ContextWordCount { get; init; }

    public int ContextCharacterCount { get; init; }

    public AutomationPolicyState LastPolicyState { get; init; }

    public LivePredictionAction LastAction { get; init; }

    public bool IsProtectionEnabled { get; init; }

    public bool IsPredictionEnabled { get; init; }

    public int BufferResets { get; init; }

    public int PredictionsEvaluated { get; init; }

    public int PredictionsCleared { get; init; }

    public bool IsOverlayDismissedForCurrentContext { get; init; }
}
