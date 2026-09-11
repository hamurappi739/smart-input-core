namespace SmartInput.Core.Models;

public enum LiveLayoutCorrectionAction
{
    None,
    BufferReset,
    TokenDiscarded,
    TokenCompleted,
    CorrectionSkippedDisabled,
    CorrectionSkippedPolicy,
    CorrectionSkippedDetection,
    CorrectionSkippedLearning,
    CorrectionSkippedUnsafeBoundary,
    CorrectionAttempted,
    CorrectionSucceeded,
    CorrectionFailed,
    AutocorrectSkippedDisabled,
    AutocorrectSkippedPolicy,
    AutocorrectSkippedDetection,
    AutocorrectSkippedLearning,
    AutocorrectAttempted,
    AutocorrectSucceeded,
    AutocorrectFailed,
    SnippetSkippedDisabled,
    SnippetSkippedPolicy,
    SnippetSkippedNoMatch,
    SnippetSkippedLearning,
    SnippetAttempted,
    SnippetSucceeded,
    SnippetFailed,
    PunctuationSkippedDisabled,
    PunctuationSkippedPolicy,
    PunctuationSkippedDetection,
    PunctuationSkippedLearning,
    PunctuationAttempted,
    PunctuationSucceeded,
    PunctuationFailed,
}

public sealed class LiveLayoutCorrectionStatus
{
    public int TokensCompleted { get; init; }

    public int CandidatesDetected { get; init; }

    public int CorrectionsAttempted { get; init; }

    public int CorrectionsSucceeded { get; init; }

    public int CorrectionsBlocked { get; init; }

    public int BufferResets { get; init; }

    public int BoundariesSuppressed { get; init; }

    public int BoundariesDelivered { get; init; }

    public int CurrentTokenLength { get; init; }

    public int CurrentSnippetTriggerLength { get; init; }

    public AutomationPolicyState LastPolicyState { get; init; }

    public LiveLayoutCorrectionAction LastAction { get; init; }

    public bool IsAutomaticLayoutEnabled { get; init; }

    public bool IsAutocorrectEnabled { get; init; }

    public bool IsSnippetsEnabled { get; init; }

    public int AutocorrectCandidatesDetected { get; init; }

    public int AutocorrectAttempts { get; init; }

    public int AutocorrectSucceeded { get; init; }

    public int AutocorrectBlocked { get; init; }

    public int SnippetExpansionsAttempted { get; init; }

    public int SnippetExpansionsSucceeded { get; init; }

    public int SnippetExpansionsBlocked { get; init; }

    public bool IsPunctuationEnabled { get; init; }

    public int PunctuationAttempts { get; init; }

    public int PunctuationSucceeded { get; init; }

    public int PunctuationBlocked { get; init; }

    public bool IsProtectionEnabled { get; init; }
}
