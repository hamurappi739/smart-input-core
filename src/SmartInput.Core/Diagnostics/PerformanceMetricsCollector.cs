using Microsoft.Extensions.Logging;
using SmartInput.Platform.Abstractions.Diagnostics;

namespace SmartInput.Core.Diagnostics;

public interface IPerformanceDebugLoggingGate
{
    bool IsEnabled { get; }
}

public sealed class SettingsPerformanceDebugLoggingGate : IPerformanceDebugLoggingGate
{
    private readonly Services.ISettingsService _settingsService;

    public SettingsPerformanceDebugLoggingGate(Services.ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsEnabled => _settingsService.Current.PerformanceDebugLoggingEnabled;
}

public sealed class PerformanceMetricsCollector : IPerformanceMetricsRecorder
{
    private readonly RollingDurationAggregate _hookCallback = new();
    private readonly RollingDurationAggregate _inputDispatchLatency = new();
    private readonly RollingDurationAggregate _layoutEvaluation = new();
    private readonly RollingDurationAggregate _autocorrectEvaluation = new();
    private readonly RollingDurationAggregate _snippetEvaluation = new();
    private readonly RollingDurationAggregate _textReplacement = new();
    private readonly RollingDurationAggregate _overlayUpdate = new();
    private readonly RollingDurationAggregate _hotkeyHandling = new();

    private readonly IPerformanceDebugLoggingGate? _debugLoggingGate;
    private readonly ILogger<PerformanceMetricsCollector>? _logger;

    private int _inputQueueDepth;
    private int _inputQueueHighWaterMark;
    private long _uncertainInputEvents;
    private long _droppedInputEvents;
    private long _replacementSuccessCount;
    private long _replacementAbortedCount;
    private long _replacementFailedCount;
    private long _replacementBlockedCount;
    private long _replacementTimeoutCount;
    private long _replacementNotSupportedCount;
    private readonly long[] _livePipelineStages = new long[Enum.GetValues<LivePipelineStage>().Length];

    public PerformanceMetricsCollector()
    {
    }

    public PerformanceMetricsCollector(
        IPerformanceDebugLoggingGate debugLoggingGate,
        ILogger<PerformanceMetricsCollector> logger)
    {
        _debugLoggingGate = debugLoggingGate;
        _logger = logger;
    }

    public void RecordDuration(PerformanceMetricKind kind, double milliseconds)
    {
        GetAggregate(kind).Record(milliseconds);
        LogDuration(kind, milliseconds, null);
    }

    public void RecordLivePipelineStage(LivePipelineStage stage)
    {
        var index = (int)stage;
        if ((uint)index < (uint)_livePipelineStages.Length)
        {
            Interlocked.Increment(ref _livePipelineStages[index]);
        }
    }

    public void RecordTextReplacementOutcome(TextReplacementOutcomeKind outcome, double milliseconds)
    {
        _textReplacement.Record(milliseconds);
        IncrementReplacementOutcome(outcome);
        LogDuration(PerformanceMetricKind.TextReplacement, milliseconds, outcome);
    }

    public void RecordInputQueueDepth(int depth)
    {
        if (depth < 0)
        {
            depth = 0;
        }

        Interlocked.Exchange(ref _inputQueueDepth, depth);

        var currentHigh = Volatile.Read(ref _inputQueueHighWaterMark);
        while (depth > currentHigh)
        {
            Interlocked.CompareExchange(ref _inputQueueHighWaterMark, depth, currentHigh);
            currentHigh = Volatile.Read(ref _inputQueueHighWaterMark);
        }
    }

    public void RecordUncertainInputEvent()
    {
        Interlocked.Increment(ref _uncertainInputEvents);
    }

    public void RecordDroppedInputEvent()
    {
        Interlocked.Increment(ref _droppedInputEvents);
    }

    public PerformanceMetricsSnapshot GetSnapshot()
    {
        return new PerformanceMetricsSnapshot
        {
            LivePipeline = new LivePipelineStageSnapshot
            {
                HookObserved = ReadLivePipelineStage(LivePipelineStage.HookObserved),
                HookFiltered = ReadLivePipelineStage(LivePipelineStage.HookFiltered),
                CharacterResolved = ReadLivePipelineStage(LivePipelineStage.CharacterResolved),
                BoundaryIntercepted = ReadLivePipelineStage(LivePipelineStage.BoundaryIntercepted),
                PolicyEvaluated = ReadLivePipelineStage(LivePipelineStage.PolicyEvaluated),
                DecisionEvaluated = ReadLivePipelineStage(LivePipelineStage.DecisionEvaluated),
                ReplacementAttempted = ReadLivePipelineStage(LivePipelineStage.ReplacementAttempted),
                ReplacementResult = ReadLivePipelineStage(LivePipelineStage.ReplacementResult),
                BoundaryDelivered = ReadLivePipelineStage(LivePipelineStage.BoundaryDelivered),
            },
            HookCallback = MapSnapshot(_hookCallback.GetSnapshot()),
            InputDispatchLatency = MapSnapshot(_inputDispatchLatency.GetSnapshot()),
            LayoutEvaluation = MapSnapshot(_layoutEvaluation.GetSnapshot()),
            AutocorrectEvaluation = MapSnapshot(_autocorrectEvaluation.GetSnapshot()),
            SnippetEvaluation = MapSnapshot(_snippetEvaluation.GetSnapshot()),
            TextReplacement = MapSnapshot(_textReplacement.GetSnapshot()),
            OverlayUpdate = MapSnapshot(_overlayUpdate.GetSnapshot()),
            HotkeyHandling = MapSnapshot(_hotkeyHandling.GetSnapshot()),
            InputQueueDepth = Volatile.Read(ref _inputQueueDepth),
            InputQueueHighWaterMark = Volatile.Read(ref _inputQueueHighWaterMark),
            UncertainInputEvents = Interlocked.Read(ref _uncertainInputEvents),
            DroppedInputEvents = Interlocked.Read(ref _droppedInputEvents),
            ReplacementSuccessCount = Interlocked.Read(ref _replacementSuccessCount),
            ReplacementAbortedCount = Interlocked.Read(ref _replacementAbortedCount),
            ReplacementFailedCount = Interlocked.Read(ref _replacementFailedCount),
            ReplacementBlockedCount = Interlocked.Read(ref _replacementBlockedCount),
            ReplacementTimeoutCount = Interlocked.Read(ref _replacementTimeoutCount),
            ReplacementNotSupportedCount = Interlocked.Read(ref _replacementNotSupportedCount),
        };
    }

    public void Reset()
    {
        _hookCallback.Reset();
        _inputDispatchLatency.Reset();
        _layoutEvaluation.Reset();
        _autocorrectEvaluation.Reset();
        _snippetEvaluation.Reset();
        _textReplacement.Reset();
        _overlayUpdate.Reset();
        _hotkeyHandling.Reset();

        Interlocked.Exchange(ref _inputQueueDepth, 0);
        Interlocked.Exchange(ref _inputQueueHighWaterMark, 0);
        Interlocked.Exchange(ref _uncertainInputEvents, 0);
        Interlocked.Exchange(ref _droppedInputEvents, 0);
        Interlocked.Exchange(ref _replacementSuccessCount, 0);
        Interlocked.Exchange(ref _replacementAbortedCount, 0);
        Interlocked.Exchange(ref _replacementFailedCount, 0);
        Interlocked.Exchange(ref _replacementBlockedCount, 0);
        Interlocked.Exchange(ref _replacementTimeoutCount, 0);
        Interlocked.Exchange(ref _replacementNotSupportedCount, 0);
        for (var index = 0; index < _livePipelineStages.Length; index++)
        {
            Interlocked.Exchange(ref _livePipelineStages[index], 0);
        }
    }

    private RollingDurationAggregate GetAggregate(PerformanceMetricKind kind)
    {
        return kind switch
        {
            PerformanceMetricKind.HookCallback => _hookCallback,
            PerformanceMetricKind.InputDispatchLatency => _inputDispatchLatency,
            PerformanceMetricKind.LayoutEvaluation => _layoutEvaluation,
            PerformanceMetricKind.AutocorrectEvaluation => _autocorrectEvaluation,
            PerformanceMetricKind.SnippetEvaluation => _snippetEvaluation,
            PerformanceMetricKind.TextReplacement => _textReplacement,
            PerformanceMetricKind.OverlayUpdate => _overlayUpdate,
            PerformanceMetricKind.HotkeyHandling => _hotkeyHandling,
            _ => _hookCallback,
        };
    }

    private void IncrementReplacementOutcome(TextReplacementOutcomeKind outcome)
    {
        switch (outcome)
        {
            case TextReplacementOutcomeKind.Success:
                Interlocked.Increment(ref _replacementSuccessCount);
                break;
            case TextReplacementOutcomeKind.Aborted:
                Interlocked.Increment(ref _replacementAbortedCount);
                break;
            case TextReplacementOutcomeKind.Failed:
                Interlocked.Increment(ref _replacementFailedCount);
                break;
            case TextReplacementOutcomeKind.Blocked:
                Interlocked.Increment(ref _replacementBlockedCount);
                break;
            case TextReplacementOutcomeKind.Timeout:
                Interlocked.Increment(ref _replacementTimeoutCount);
                break;
            case TextReplacementOutcomeKind.NotSupported:
                Interlocked.Increment(ref _replacementNotSupportedCount);
                break;
        }
    }

    private void LogDuration(
        PerformanceMetricKind kind,
        double milliseconds,
        TextReplacementOutcomeKind? replacementOutcome)
    {
        if (_debugLoggingGate is null || !_debugLoggingGate.IsEnabled || _logger is null)
        {
            return;
        }

        if (replacementOutcome is null)
        {
            _logger.LogDebug(
                "Performance metric {MetricKind} durationMs={DurationMs:F3}",
                kind,
                milliseconds);
            return;
        }

        _logger.LogDebug(
            "Performance metric {MetricKind} outcome={Outcome} durationMs={DurationMs:F3}",
            kind,
            replacementOutcome.Value,
            milliseconds);
    }

    private static DurationMetricSnapshot MapSnapshot(DurationAggregateSnapshot snapshot)
    {
        return new DurationMetricSnapshot
        {
            SampleCount = snapshot.SampleCount,
            AverageMilliseconds = snapshot.AverageMilliseconds,
            MaxMilliseconds = snapshot.MaxMilliseconds,
            Percentile95Milliseconds = snapshot.Percentile95Milliseconds,
        };
    }

    private long ReadLivePipelineStage(LivePipelineStage stage)
    {
        return Interlocked.Read(ref _livePipelineStages[(int)stage]);
    }
}

public sealed class NullPerformanceMetricsRecorder : IPerformanceMetricsRecorder
{
    public static NullPerformanceMetricsRecorder Instance { get; } = new();

    public void RecordDuration(PerformanceMetricKind kind, double milliseconds)
    {
    }

    public void RecordTextReplacementOutcome(TextReplacementOutcomeKind outcome, double milliseconds)
    {
    }

    public void RecordInputQueueDepth(int depth)
    {
    }

    public void RecordUncertainInputEvent()
    {
    }

    public void RecordDroppedInputEvent()
    {
    }

    public PerformanceMetricsSnapshot GetSnapshot() => new();

    public void RecordLivePipelineStage(LivePipelineStage stage)
    {
    }

    public void Reset()
    {
    }
}
