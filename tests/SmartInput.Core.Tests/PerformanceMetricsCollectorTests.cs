using System.Diagnostics;
using System.Reflection;
using SmartInput.Core.Diagnostics;
using SmartInput.Platform.Abstractions.Diagnostics;

namespace SmartInput.Core.Tests;

public class PerformanceMetricsCollectorTests
{
    [Fact]
    public void RecordDuration_AggregatesAverageMaxAndPercentile()
    {
        var collector = new PerformanceMetricsCollector();

        collector.RecordDuration(PerformanceMetricKind.HookCallback, 1);
        collector.RecordDuration(PerformanceMetricKind.HookCallback, 3);
        collector.RecordDuration(PerformanceMetricKind.HookCallback, 2);
        collector.RecordDuration(PerformanceMetricKind.HookCallback, 10);

        var snapshot = collector.GetSnapshot();

        Assert.Equal(4, snapshot.HookCallback.SampleCount);
        Assert.Equal(4, snapshot.HookCallback.AverageMilliseconds);
        Assert.Equal(10, snapshot.HookCallback.MaxMilliseconds);
        Assert.Equal(10, snapshot.HookCallback.Percentile95Milliseconds);
    }

    [Fact]
    public void RollingWindow_KeepsOnlyMostRecent256Samples()
    {
        var collector = new PerformanceMetricsCollector();

        for (var i = 1; i <= 300; i++)
        {
            collector.RecordDuration(PerformanceMetricKind.LayoutEvaluation, i);
        }

        var snapshot = collector.GetSnapshot();

        Assert.Equal(RollingDurationAggregate.MaxSamples, snapshot.LayoutEvaluation.SampleCount);
        Assert.True(snapshot.LayoutEvaluation.AverageMilliseconds > 40);
        Assert.Equal(300, snapshot.LayoutEvaluation.MaxMilliseconds);
    }

    [Fact]
    public void Reset_ClearsAllMetrics()
    {
        var collector = new PerformanceMetricsCollector();

        collector.RecordDuration(PerformanceMetricKind.HotkeyHandling, 5);
        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Success, 12);
        collector.RecordInputQueueDepth(7);
        collector.RecordUncertainInputEvent();
        collector.RecordDroppedInputEvent();

        collector.Reset();
        var snapshot = collector.GetSnapshot();

        Assert.Equal(0, snapshot.HotkeyHandling.SampleCount);
        Assert.Equal(0, snapshot.TextReplacement.SampleCount);
        Assert.Equal(0, snapshot.InputQueueDepth);
        Assert.Equal(0, snapshot.InputQueueHighWaterMark);
        Assert.Equal(0, snapshot.UncertainInputEvents);
        Assert.Equal(0, snapshot.DroppedInputEvents);
        Assert.Equal(0, snapshot.ReplacementSuccessCount);
    }

    [Fact]
    public void Snapshot_ContainsOnlyNumericAndEnumValues()
    {
        var collector = new PerformanceMetricsCollector();
        collector.RecordDuration(PerformanceMetricKind.OverlayUpdate, 2.5);
        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Aborted, 1.25);

        var snapshot = collector.GetSnapshot();
        var formatted = PerformanceMetricsFormatter.Format(snapshot);

        Assert.DoesNotContain("abc", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", formatted, StringComparison.OrdinalIgnoreCase);
        AssertAllPublicPropertiesAreNumericOrEnum(snapshot);
    }

    [Fact]
    public void RecordTextReplacementOutcome_TracksSuccessAbortFailureAndTimeoutCounters()
    {
        var collector = new PerformanceMetricsCollector();

        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Success, 1);
        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Aborted, 2);
        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Failed, 3);
        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Blocked, 4);
        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Timeout, 5);
        collector.RecordTextReplacementOutcome(TextReplacementOutcomeKind.NotSupported, 6);

        var snapshot = collector.GetSnapshot();

        Assert.Equal(1, snapshot.ReplacementSuccessCount);
        Assert.Equal(1, snapshot.ReplacementAbortedCount);
        Assert.Equal(1, snapshot.ReplacementFailedCount);
        Assert.Equal(1, snapshot.ReplacementBlockedCount);
        Assert.Equal(1, snapshot.ReplacementTimeoutCount);
        Assert.Equal(1, snapshot.ReplacementNotSupportedCount);
        Assert.Equal(6, snapshot.TextReplacement.SampleCount);
    }

    [Fact]
    public void RecordInputQueueDepth_TracksCurrentAndHighWaterMark()
    {
        var collector = new PerformanceMetricsCollector();

        collector.RecordInputQueueDepth(3);
        collector.RecordInputQueueDepth(8);
        collector.RecordInputQueueDepth(2);

        var snapshot = collector.GetSnapshot();

        Assert.Equal(2, snapshot.InputQueueDepth);
        Assert.Equal(8, snapshot.InputQueueHighWaterMark);
    }

    [Fact]
    public void LivePipelineTelemetry_IsAggregateOnlyAndResettable()
    {
        var collector = new PerformanceMetricsCollector();

        collector.RecordLivePipelineStage(LivePipelineStage.HookObserved);
        collector.RecordLivePipelineStage(LivePipelineStage.CharacterResolved);
        collector.RecordLivePipelineStage(LivePipelineStage.BoundaryIntercepted);
        collector.RecordLivePipelineStage(LivePipelineStage.PolicyEvaluated);
        collector.RecordLivePipelineStage(LivePipelineStage.DecisionEvaluated);
        collector.RecordLivePipelineStage(LivePipelineStage.ReplacementAttempted);
        collector.RecordLivePipelineStage(LivePipelineStage.ReplacementResult);
        collector.RecordLivePipelineStage(LivePipelineStage.BoundaryDelivered);

        var snapshot = collector.GetSnapshot().LivePipeline;

        Assert.Equal(1, snapshot.HookObserved);
        Assert.Equal(1, snapshot.CharacterResolved);
        Assert.Equal(1, snapshot.BoundaryIntercepted);
        Assert.Equal(1, snapshot.PolicyEvaluated);
        Assert.Equal(1, snapshot.DecisionEvaluated);
        Assert.Equal(1, snapshot.ReplacementAttempted);
        Assert.Equal(1, snapshot.ReplacementResult);
        Assert.Equal(1, snapshot.BoundaryDelivered);

        collector.Reset();
        Assert.Equal(0, collector.GetSnapshot().LivePipeline.HookObserved);
    }

    [Fact]
    public async Task ConcurrentTelemetryWritesAndSnapshotsRemainBounded()
    {
        var collector = new PerformanceMetricsCollector();
        const int writerCount = 8;
        const int samplesPerWriter = 2_000;
        using var start = new ManualResetEventSlim(false);

        var writerTasks = Enumerable.Range(0, writerCount)
            .Select(worker => Task.Run(() =>
            {
                start.Wait();
                for (var sample = 0; sample < samplesPerWriter; sample++)
                {
                    collector.RecordDuration(PerformanceMetricKind.HookCallback, sample % 11);
                    collector.RecordLivePipelineStage(LivePipelineStage.HookObserved);
                    collector.RecordInputQueueDepth(worker + 1);
                }
            }))
            .ToArray();
        var allWriters = Task.WhenAll(writerTasks);

        var snapshotTask = Task.Run(() =>
        {
            start.Wait();
            while (!allWriters.IsCompleted)
            {
                var snapshot = collector.GetSnapshot();
                Assert.InRange(snapshot.HookCallback.SampleCount, 0, RollingDurationAggregate.MaxSamples);
                Assert.InRange(snapshot.InputQueueDepth, 0, writerCount);
                Assert.InRange(snapshot.InputQueueHighWaterMark, 0, writerCount);
                Assert.True(snapshot.LivePipeline.HookObserved >= 0);
            }
        });

        start.Set();
        await allWriters;
        await snapshotTask;

        var final = collector.GetSnapshot();
        Assert.Equal(writerCount * samplesPerWriter, final.LivePipeline.HookObserved);
        Assert.Equal(RollingDurationAggregate.MaxSamples, final.HookCallback.SampleCount);
        Assert.InRange(final.InputQueueDepth, 1, writerCount);
        Assert.Equal(writerCount, final.InputQueueHighWaterMark);
    }

    [Fact]
    public void OverlayAndHotkeyMetrics_AreRecordedIndependently()
    {
        var collector = new PerformanceMetricsCollector();

        collector.RecordDuration(PerformanceMetricKind.OverlayUpdate, 4);
        collector.RecordDuration(PerformanceMetricKind.HotkeyHandling, 6);

        var snapshot = collector.GetSnapshot();

        Assert.Equal(1, snapshot.OverlayUpdate.SampleCount);
        Assert.Equal(4, snapshot.OverlayUpdate.AverageMilliseconds);
        Assert.Equal(1, snapshot.HotkeyHandling.SampleCount);
        Assert.Equal(6, snapshot.HotkeyHandling.AverageMilliseconds);
    }

    [Fact]
    public void HookRecording_CompletesQuicklyForManySamples()
    {
        var collector = new PerformanceMetricsCollector();
        var startedAt = Stopwatch.GetTimestamp();

        for (var i = 0; i < 10_000; i++)
        {
            collector.RecordDuration(PerformanceMetricKind.HookCallback, 0.01);
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        Assert.True(elapsedMs < 500, $"Hook metric recording took {elapsedMs:F1} ms for 10k samples.");
        Assert.Equal(RollingDurationAggregate.MaxSamples, collector.GetSnapshot().HookCallback.SampleCount);
    }

    [Fact]
    public void NullRecorder_IsNoOp()
    {
        var recorder = NullPerformanceMetricsRecorder.Instance;

        recorder.RecordDuration(PerformanceMetricKind.HookCallback, 1);
        recorder.RecordTextReplacementOutcome(TextReplacementOutcomeKind.Success, 1);
        recorder.RecordInputQueueDepth(5);
        recorder.RecordUncertainInputEvent();
        recorder.RecordDroppedInputEvent();

        var snapshot = recorder.GetSnapshot();
        recorder.Reset();

        Assert.Equal(0, snapshot.HookCallback.SampleCount);
        Assert.Equal(0, snapshot.ReplacementSuccessCount);
    }

    private static void AssertAllPublicPropertiesAreNumericOrEnum(object instance)
    {
        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = property.GetValue(instance);
            if (value is null)
            {
                continue;
            }

            if (property.PropertyType.IsEnum)
            {
                continue;
            }

            if (property.PropertyType == typeof(string))
            {
                Assert.Fail($"Snapshot property {property.Name} is a string.");
            }

            if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
            {
                AssertAllPublicPropertiesAreNumericOrEnum(value);
                continue;
            }

            Assert.True(
                property.PropertyType == typeof(int)
                    || property.PropertyType == typeof(long)
                    || property.PropertyType == typeof(double)
                    || property.PropertyType == typeof(float)
                    || property.PropertyType == typeof(decimal)
                    || property.PropertyType == typeof(bool),
                $"Snapshot property {property.Name} has unsupported type {property.PropertyType.Name}.");
        }
    }
}
