using System.Text;
using SmartInput.Platform.Abstractions.Diagnostics;

namespace SmartInput.Core.Diagnostics;

public static class PerformanceMetricsFormatter
{
    public static string Format(PerformanceMetricsSnapshot snapshot)
    {
        var builder = new StringBuilder(1024);
        AppendDuration(builder, "обработчик_клавиш", snapshot.HookCallback);
        AppendDuration(builder, "задержка_очереди", snapshot.InputDispatchLatency);
        AppendDuration(builder, "проверка_раскладки", snapshot.LayoutEvaluation);
        AppendDuration(builder, "проверка_автокоррекции", snapshot.AutocorrectEvaluation);
        AppendDuration(builder, "проверка_шаблонов", snapshot.SnippetEvaluation);
        AppendDuration(builder, "замена_текста", snapshot.TextReplacement);
        AppendDuration(builder, "обновление_подсказки", snapshot.OverlayUpdate);
        AppendDuration(builder, "обработка_горячей_клавиши", snapshot.HotkeyHandling);

        builder.Append("глубина_очереди=").Append(snapshot.InputQueueDepth).Append("; ");
        builder.Append("максимум_очереди=").Append(snapshot.InputQueueHighWaterMark).Append("; ");
        builder.Append("неопределённых_событий=").Append(snapshot.UncertainInputEvents).Append("; ");
        builder.Append("пропущенных_событий=").Append(snapshot.DroppedInputEvents).Append("; ");
        builder.Append("успешных_замен=").Append(snapshot.ReplacementSuccessCount).Append("; ");
        builder.Append("прерванных_замен=").Append(snapshot.ReplacementAbortedCount).Append("; ");
        builder.Append("ошибок_замены=").Append(snapshot.ReplacementFailedCount).Append("; ");
        builder.Append("заблокированных_замен=").Append(snapshot.ReplacementBlockedCount).Append("; ");
        builder.Append("таймаутов_замены=").Append(snapshot.ReplacementTimeoutCount).Append("; ");
        builder.Append("неподдерживаемых_замен=").Append(snapshot.ReplacementNotSupportedCount);
        var live = snapshot.LivePipeline;
        builder.Append("; live_hook=").Append(live.HookObserved)
            .Append("; live_filtered=").Append(live.HookFiltered)
            .Append("; live_resolved=").Append(live.CharacterResolved)
            .Append("; live_boundary_intercepted=").Append(live.BoundaryIntercepted)
            .Append("; live_policy=").Append(live.PolicyEvaluated)
            .Append("; live_decision=").Append(live.DecisionEvaluated)
            .Append("; live_replacement_attempted=").Append(live.ReplacementAttempted)
            .Append("; live_replacement_result=").Append(live.ReplacementResult)
            .Append("; live_boundary_delivered=").Append(live.BoundaryDelivered);

        return builder.ToString();
    }

    private static void AppendDuration(StringBuilder builder, string label, DurationMetricSnapshot metric)
    {
        builder.Append(label)
            .Append("(количество=").Append(metric.SampleCount)
            .Append(", среднее=").Append(metric.AverageMilliseconds.ToString("F3"))
            .Append(", максимум=").Append(metric.MaxMilliseconds.ToString("F3"))
            .Append(", p95=").Append(metric.Percentile95Milliseconds.ToString("F3"))
            .Append("ms); ");
    }
}
