namespace SmartInput.Platform.Abstractions.Diagnostics;

/// <summary>
/// Aggregate-only checkpoints for the real keyboard input path. These values
/// must never carry token, replacement, window-title or process data.
/// </summary>
public enum LivePipelineStage
{
    HookObserved = 0,
    HookFiltered = 1,
    CharacterResolved = 2,
    BoundaryIntercepted = 3,
    PolicyEvaluated = 4,
    DecisionEvaluated = 5,
    ReplacementAttempted = 6,
    ReplacementResult = 7,
    BoundaryDelivered = 8,
}

public sealed class LivePipelineStageSnapshot
{
    public long HookObserved { get; init; }
    public long HookFiltered { get; init; }
    public long CharacterResolved { get; init; }
    public long BoundaryIntercepted { get; init; }
    public long PolicyEvaluated { get; init; }
    public long DecisionEvaluated { get; init; }
    public long ReplacementAttempted { get; init; }
    public long ReplacementResult { get; init; }
    public long BoundaryDelivered { get; init; }
}
