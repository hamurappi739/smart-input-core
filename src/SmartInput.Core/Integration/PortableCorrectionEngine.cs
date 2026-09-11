using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Integration;

/// <summary>
/// Stable, transport-independent request for the correction engine.
/// This DTO is intentionally independent of Windows hooks and UI state so it
/// can be called from another application or through a JSON/IPC adapter.
/// </summary>
public sealed record PortableCorrectionRequest
{
    public required string Token { get; init; }

    public bool LayoutEnabled { get; init; } = true;

    public bool AutocorrectEnabled { get; init; } = true;

    public string? DominantLanguage { get; init; }

    public int RussianTokenCount { get; init; }

    public int EnglishTokenCount { get; init; }

    public int ContextTokenCount { get; init; }

    public int ContextCharacterCount { get; init; }

    public double? CandidateThreshold { get; init; }

    public double? WaitThreshold { get; init; }
}

/// <summary>
/// Stable response contract. No text is applied by this API.
/// </summary>
public sealed record PortableCorrectionResponse
{
    public required string OriginalToken { get; init; }

    public string? ReplacementToken { get; init; }

    public required string Decision { get; init; }

    public required string Kind { get; init; }

    public double ConfidenceScore { get; init; }

    public string? LayoutDirection { get; init; }

    public string? TargetInputLanguage { get; init; }

    public required string Reason { get; init; }

    public bool CanApply => Decision == "apply" && !string.IsNullOrEmpty(ReplacementToken);
}

public interface IPortableCorrectionEngine
{
    PortableCorrectionResponse Evaluate(PortableCorrectionRequest request);
}

/// <summary>
/// Portable decision facade over the existing joint layout/spelling pipeline.
/// It deliberately does not call SafeTextReplacementService, SendInput or
/// clipboard APIs. The caller must pass an approved response to its own
/// safety/apply transaction.
/// </summary>
public sealed class PortableCorrectionEngine : IPortableCorrectionEngine
{
    private readonly IJointCorrectionDecisionService _decisionService;
    private readonly IAutocorrectDictionary _dictionary;

    public PortableCorrectionEngine(
        IJointCorrectionDecisionService decisionService,
        IAutocorrectDictionary dictionary)
    {
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    }

    public PortableCorrectionResponse Evaluate(PortableCorrectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var autocorrectionOptions = new AutocorrectionOptions
        {
            CandidateThreshold = request.CandidateThreshold ?? AutocorrectionOptions.DefaultCandidateThreshold,
            WaitThreshold = request.WaitThreshold ?? AutocorrectionOptions.DefaultWaitThreshold,
        };
        var layoutOptions = new WrongLayoutDetectionOptions
        {
            CandidateThreshold = request.CandidateThreshold ?? WrongLayoutDetectionOptions.DefaultCandidateThreshold,
            WaitThreshold = request.WaitThreshold ?? WrongLayoutDetectionOptions.DefaultWaitThreshold,
        };

        var hint = CreateLanguageHint(request);
        var result = _decisionService.Evaluate(
            request.Token,
            _dictionary,
            request.LayoutEnabled,
            request.AutocorrectEnabled,
            autocorrectionOptions,
            layoutOptions,
            hint);

        return new PortableCorrectionResponse
        {
            OriginalToken = result.OriginalToken,
            ReplacementToken = result.ReplacementToken,
            Decision = result.Recommendation switch
            {
                JointCorrectionRecommendation.Apply => "apply",
                JointCorrectionRecommendation.Wait => "wait",
                _ => "no_change",
            },
            Kind = result.Kind.ToString().ToLowerInvariant(),
            ConfidenceScore = result.ConfidenceScore,
            LayoutDirection = result.LayoutDirection?.ToString(),
            TargetInputLanguage = result.TargetInputLanguage?.ToString(),
            Reason = BuildReason(result),
        };
    }

    private static SentenceLanguageHint CreateLanguageHint(PortableCorrectionRequest request)
    {
        var dominantLanguage = request.DominantLanguage?.Trim().ToLowerInvariant() switch
        {
            "ru" or "russian" => (TypingLanguage?)TypingLanguage.Russian,
            "en" or "english" => (TypingLanguage?)TypingLanguage.English,
            _ => (TypingLanguage?)null,
        };

        return new SentenceLanguageHint(
            dominantLanguage,
            Math.Max(0, request.RussianTokenCount),
            Math.Max(0, request.EnglishTokenCount),
            Math.Max(0, request.ContextTokenCount),
            Math.Max(0, request.ContextCharacterCount));
    }

    private static string BuildReason(JointCorrectionDecisionResult result)
    {
        return result.Recommendation switch
        {
            JointCorrectionRecommendation.Apply => "candidate_passed_policy",
            JointCorrectionRecommendation.Wait => "candidate_is_ambiguous_or_below_apply_confidence",
            _ when string.IsNullOrWhiteSpace(result.OriginalToken) => "empty_token",
            _ => "no_safe_candidate",
        };
    }
}
