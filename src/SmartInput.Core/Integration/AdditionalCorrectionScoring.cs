using System.Security.Cryptography;
using System.Text;

namespace SmartInput.Core.Integration;

/// <summary>
/// Optional result produced by a future local scorer/model. The scorer is an
/// observer of the Core contract; it is never an instruction to edit text.
/// </summary>
public sealed record AdditionalCorrectionScore(
    string Decision,
    string? ReplacementToken,
    double ConfidenceScore,
    string Kind,
    string ReasonCode = "unspecified")
{
    public bool IsWellFormed =>
        Decision is "apply" or "wait" or "no_change"
        && ConfidenceScore is >= 0 and <= 1
        && (Decision != "apply" || !string.IsNullOrWhiteSpace(ReplacementToken));
}

/// <summary>
/// Extension point for a local model or another candidate scorer. Implementors
/// must not call SendInput, clipboard APIs, or the live replacement pipeline.
/// </summary>
public interface IAdditionalCorrectionScorer
{
    AdditionalCorrectionScore Evaluate(PortableCorrectionRequest request);
}

public enum AdditionalScorerComparisonOutcome
{
    CoreNoChangeScorerNoChange,
    CoreWaitScorerWait,
    CoreApplyScorerApplySame,
    CoreApplyScorerDisagrees,
    CoreApplyScorerNoChange,
    CoreApplyScorerWait,
    CoreWaitScorerApply,
    CoreWaitScorerNoChange,
    CoreNoChangeScorerApply,
    CoreNoChangeScorerWait,
    InvalidScorerResult,
}

/// <summary>
/// Privacy-safe audit record. Candidate strings are represented by SHA-256
/// digests; the source token is deliberately not retained.
/// </summary>
public sealed record AdditionalScorerComparisonResult(
    AdditionalScorerComparisonOutcome Outcome,
    string CoreDecision,
    string CoreKind,
    string? CoreReplacementDigest,
    string ScorerDecision,
    string ScorerKind,
    string? ScorerReplacementDigest,
    double ScorerConfidenceScore,
    string ScorerReasonCode);

/// <summary>
/// Runs Core and an optional scorer side-by-side. This service is audit-only:
/// it never selects a replacement and never invokes a text-replacement API.
/// </summary>
public sealed class AdditionalCorrectionScoringAuditService
{
    private readonly IPortableCorrectionEngine _core;

    public AdditionalCorrectionScoringAuditService(IPortableCorrectionEngine core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
    }

    public AdditionalScorerComparisonResult Compare(
        PortableCorrectionRequest request,
        IAdditionalCorrectionScorer scorer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scorer);

        var core = _core.Evaluate(request);
        var model = scorer.Evaluate(request);
        if (model is null || !model.IsWellFormed)
        {
            return new AdditionalScorerComparisonResult(
                AdditionalScorerComparisonOutcome.InvalidScorerResult,
                core.Decision,
                core.Kind,
                DigestOrNull(core.ReplacementToken),
                model?.Decision ?? "invalid",
                model?.Kind ?? "invalid",
                DigestOrNull(model?.ReplacementToken),
                model?.ConfidenceScore ?? 0,
                model?.ReasonCode ?? "invalid");
        }

        var outcome = (core.Decision, model.Decision) switch
        {
            ("no_change", "no_change") => AdditionalScorerComparisonOutcome.CoreNoChangeScorerNoChange,
            ("wait", "wait") => AdditionalScorerComparisonOutcome.CoreWaitScorerWait,
            ("apply", "apply") when string.Equals(core.ReplacementToken, model.ReplacementToken, StringComparison.Ordinal)
                => AdditionalScorerComparisonOutcome.CoreApplyScorerApplySame,
            ("apply", "apply") => AdditionalScorerComparisonOutcome.CoreApplyScorerDisagrees,
            ("apply", "no_change") => AdditionalScorerComparisonOutcome.CoreApplyScorerNoChange,
            ("apply", "wait") => AdditionalScorerComparisonOutcome.CoreApplyScorerWait,
            ("wait", "apply") => AdditionalScorerComparisonOutcome.CoreWaitScorerApply,
            ("wait", "no_change") => AdditionalScorerComparisonOutcome.CoreWaitScorerNoChange,
            ("no_change", "apply") => AdditionalScorerComparisonOutcome.CoreNoChangeScorerApply,
            ("no_change", "wait") => AdditionalScorerComparisonOutcome.CoreNoChangeScorerWait,
            _ => AdditionalScorerComparisonOutcome.CoreApplyScorerDisagrees,
        };

        return new AdditionalScorerComparisonResult(
            outcome,
            core.Decision,
            core.Kind,
            DigestOrNull(core.ReplacementToken),
            model.Decision,
            model.Kind,
            DigestOrNull(model.ReplacementToken),
            model.ConfidenceScore,
            model.ReasonCode);
    }

    private static string? DigestOrNull(string? value)
        => string.IsNullOrEmpty(value) ? null : Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
