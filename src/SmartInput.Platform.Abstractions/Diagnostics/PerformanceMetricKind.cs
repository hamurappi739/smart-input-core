namespace SmartInput.Platform.Abstractions.Diagnostics;

public enum PerformanceMetricKind
{
    HookCallback,
    InputDispatchLatency,
    LayoutEvaluation,
    AutocorrectEvaluation,
    SnippetEvaluation,
    TextReplacement,
    OverlayUpdate,
    HotkeyHandling,
}

public enum TextReplacementOutcomeKind
{
    Success,
    Aborted,
    Failed,
    Blocked,
    Timeout,
    NotSupported,
}
