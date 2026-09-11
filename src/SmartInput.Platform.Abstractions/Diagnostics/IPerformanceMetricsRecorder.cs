namespace SmartInput.Platform.Abstractions.Diagnostics;

public sealed class DurationMetricSnapshot
{
    public long SampleCount { get; init; }

    public double AverageMilliseconds { get; init; }

    public double MaxMilliseconds { get; init; }

    public double Percentile95Milliseconds { get; init; }
}

public sealed class PerformanceMetricsSnapshot
{
    public LivePipelineStageSnapshot LivePipeline { get; init; } = new();

    public DurationMetricSnapshot HookCallback { get; init; } = new();

    public DurationMetricSnapshot InputDispatchLatency { get; init; } = new();

    public DurationMetricSnapshot LayoutEvaluation { get; init; } = new();

    public DurationMetricSnapshot AutocorrectEvaluation { get; init; } = new();

    public DurationMetricSnapshot SnippetEvaluation { get; init; } = new();

    public DurationMetricSnapshot TextReplacement { get; init; } = new();

    public DurationMetricSnapshot OverlayUpdate { get; init; } = new();

    public DurationMetricSnapshot HotkeyHandling { get; init; } = new();

    public int InputQueueDepth { get; init; }

    public int InputQueueHighWaterMark { get; init; }

    public long UncertainInputEvents { get; init; }

    public long DroppedInputEvents { get; init; }

    public long ReplacementSuccessCount { get; init; }

    public long ReplacementAbortedCount { get; init; }

    public long ReplacementFailedCount { get; init; }

    public long ReplacementBlockedCount { get; init; }

    public long ReplacementTimeoutCount { get; init; }

    public long ReplacementNotSupportedCount { get; init; }
}

public interface IPerformanceMetricsRecorder
{
    void RecordLivePipelineStage(LivePipelineStage stage);

    void RecordDuration(PerformanceMetricKind kind, double milliseconds);

    void RecordTextReplacementOutcome(TextReplacementOutcomeKind outcome, double milliseconds);

    void RecordInputQueueDepth(int depth);

    void RecordUncertainInputEvent();

    void RecordDroppedInputEvent();

    PerformanceMetricsSnapshot GetSnapshot();

    void Reset();
}
