using System.Diagnostics;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Services;

public interface ISafeTextReplacementService
{
    Task<TextReplacementResult> ReplaceRecentTextAsync(
        string originalText,
        string replacementText,
        CancellationToken cancellationToken = default);

    Task<TextReplacementResult> InsertTextAsync(
        string text,
        CancellationToken cancellationToken = default);

    Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default);
}

public sealed class SafeTextReplacementService : ISafeTextReplacementService
{
    public const string SecureInputBlockedReason =
        "Замена текста недоступна во время защищённого ввода.";

    public const string PolicyBlockedReasonPrefix = "Замена текста заблокирована политикой безопасности: ";

    private readonly ITextReplacementService _textReplacementService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;

    public SafeTextReplacementService(
        ITextReplacementService textReplacementService,
        IAutomationSafetyService automationSafetyService,
        IPerformanceMetricsRecorder performanceMetrics)
    {
        _textReplacementService = textReplacementService;
        _automationSafetyService = automationSafetyService;
        _performanceMetrics = performanceMetrics;
    }

    public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
    {
        return ReplaceRecentTextAsync(
            Configuration.TextReplacementDemo.OriginalText,
            Configuration.TextReplacementDemo.ReplacementText,
            cancellationToken);
    }

    public async Task<TextReplacementResult> ReplaceRecentTextAsync(
        string originalText,
        string replacementText,
        CancellationToken cancellationToken = default)
    {
        var validation = TextReplacementValidator.Validate(originalText, replacementText);
        if (!validation.IsValid)
        {
            var blocked = TextReplacementResult.Blocked(validation.FailureReason!);
            RecordReplacement(blocked, 0);
            return blocked;
        }

        if (!await _automationSafetyService
                .IsOperationAllowedAsync(AutomationOperationKind.AutomaticTextReplacement, cancellationToken)
                .ConfigureAwait(false))
        {
            var reason = _automationSafetyService.GetBlockedReason(AutomationOperationKind.AutomaticTextReplacement)
                ?? "Автоматизация не разрешена в текущем контексте.";

            TextReplacementResult blocked;
            if (reason.Contains("Secure input", StringComparison.OrdinalIgnoreCase))
            {
                blocked = TextReplacementResult.Blocked(SecureInputBlockedReason);
            }
            else
            {
                blocked = TextReplacementResult.Blocked(PolicyBlockedReasonPrefix + reason);
            }

            RecordReplacement(blocked, 0);
            return blocked;
        }

        var startedAt = Stopwatch.GetTimestamp();
        _performanceMetrics.RecordLivePipelineStage(LivePipelineStage.ReplacementAttempted);
        var result = await _textReplacementService.ReplaceRecentTextAsync(
            new TextReplacementRequest
            {
                OriginalText = originalText,
                ReplacementText = replacementText,
            },
            cancellationToken).ConfigureAwait(false);
        RecordReplacement(result, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return result;
    }

    private void RecordReplacement(TextReplacementResult result, double durationMilliseconds)
    {
        _performanceMetrics.RecordLivePipelineStage(LivePipelineStage.ReplacementResult);
        _performanceMetrics.RecordTextReplacementOutcome(
            TextReplacementOutcomeMapper.Map(result),
            durationMilliseconds);
    }

    public async Task<TextReplacementResult> InsertTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInsertText(text);
        if (!validation.IsValid)
        {
            var blocked = TextReplacementResult.Blocked(validation.FailureReason!);
            RecordReplacement(blocked, 0);
            return blocked;
        }

        if (!await _automationSafetyService
                .IsOperationAllowedAsync(AutomationOperationKind.AutomaticTextReplacement, cancellationToken)
                .ConfigureAwait(false))
        {
            var reason = _automationSafetyService.GetBlockedReason(AutomationOperationKind.AutomaticTextReplacement)
                ?? "Автоматизация не разрешена в текущем контексте.";

            TextReplacementResult blocked;
            if (reason.Contains("Secure input", StringComparison.OrdinalIgnoreCase))
            {
                blocked = TextReplacementResult.Blocked(SecureInputBlockedReason);
            }
            else
            {
                blocked = TextReplacementResult.Blocked(PolicyBlockedReasonPrefix + reason);
            }

            RecordReplacement(blocked, 0);
            return blocked;
        }

        var startedAt = Stopwatch.GetTimestamp();
        _performanceMetrics.RecordLivePipelineStage(LivePipelineStage.ReplacementAttempted);
        var result = await _textReplacementService.ReplaceRecentTextAsync(
            new TextReplacementRequest
            {
                OriginalText = string.Empty,
                ReplacementText = text,
            },
            cancellationToken).ConfigureAwait(false);
        RecordReplacement(result, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return result;
    }

    private static TextReplacementValidationResult ValidateInsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TextReplacementValidationResult.Invalid("Текст для вставки не может быть пустым.");
        }

        if (text.Length > TextReplacementValidator.MaxTextLength)
        {
            return TextReplacementValidationResult.Invalid("Текст замены превышает допустимую длину.");
        }

        if (text.Any(char.IsControl))
        {
            return TextReplacementValidationResult.Invalid("Управляющие символы не поддерживаются.");
        }

        return TextReplacementValidationResult.Valid();
    }
}
