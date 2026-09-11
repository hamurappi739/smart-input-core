using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using SmartInput.Core.Models;

namespace SmartInput.Core.Integration;

/// <summary>
/// Result returned by the optional Rust scoring DLL. Candidate text is held only
/// in memory long enough to compare decisions; callers must not log it.
/// </summary>
public sealed record RustShadowCandidateResult(
    RustShadowProviderState ProviderState,
    RustShadowDecision Decision,
    RustShadowReason Reason,
    double Confidence,
    double Margin,
    string? ReplacementToken = null)
{
    public static RustShadowCandidateResult Unavailable(RustShadowProviderState state) =>
        new(state, RustShadowDecision.Keep, RustShadowReason.None, 0, 0);
}

public enum RustShadowProviderState
{
    Disabled,
    Available,
    LibraryNotFound,
    LibraryLoadFailed,
    AbiMismatch,
    InitializationFailed,
    NativeFailure,
}

public enum RustShadowDecision
{
    Keep,
    Replace,
}

public enum RustShadowReason
{
    None,
    Layout,
    Spelling,
    ProtectedContext,
    NoUsefulMapping,
    LowConfidence,
    ProtectedToken,
    LearnedCorrection,
}

/// <summary>
/// Optional, audit-only bridge to the Rust scorer. This interface never owns
/// a Windows text transaction and must never call SendInput.
/// </summary>
public interface IRustShadowCandidateProvider
{
    RustShadowProviderState State { get; }

    RustShadowCandidateResult Evaluate(string token, string context);
}

public sealed class NullRustShadowCandidateProvider : IRustShadowCandidateProvider
{
    public NullRustShadowCandidateProvider(RustShadowProviderState state = RustShadowProviderState.Disabled)
    {
        State = state;
    }

    public RustShadowProviderState State { get; }

    public RustShadowCandidateResult Evaluate(string token, string context)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(context);
        return RustShadowCandidateResult.Unavailable(State);
    }
}

public enum RustShadowComparisonOutcome
{
    ProviderUnavailable,
    NativeFailure,
    BothKeep,
    RustKeepsOwnApplies,
    RustAppliesOwnKeeps,
    RustMatchesOwnApply,
    RustDiffersFromOwnApply,
}

/// <summary>
/// A review-only classification for a possible future allow-list. This is
/// intentionally not an instruction to replace text.
/// </summary>
public enum RustShadowPromotionDisposition
{
    NotEligible,
    CandidateForHumanReview,
}

/// <summary>
/// Privacy-safe comparison record. It contains only decisions, numeric
/// metadata and SHA-256 digests; it deliberately excludes source and target
/// text.
/// </summary>
public sealed record RustShadowComparisonResult(
    RustShadowComparisonOutcome Outcome,
    RustShadowProviderState ProviderState,
    RustShadowReason RustReason,
    double RustConfidence,
    double RustMargin,
    string OwnDecision,
    string OwnKind,
    string? OwnReplacementDigest,
    string? RustReplacementDigest);

public interface IRustShadowAuditService
{
    RustShadowProviderState ProviderState { get; }

    RustShadowAuditStatus Status { get; }

    /// <param name="context">Aggregate language statistics only; raw previous
    /// words and text must never cross the shadow boundary.</param>
    void Observe(string token, string context, JointCorrectionDecisionResult ownDecision);
}

/// <summary>
/// Aggregate-only snapshot that may be shown by a separate settings process.
/// It contains no text, candidate strings, process names or window metadata.
/// </summary>
public sealed record RustShadowAuditSnapshot(
    RustShadowProviderState ProviderState,
    RustShadowAuditStatus Status);

/// <summary>
/// Reads an aggregate Rust shadow snapshot. Implementations must fail closed
/// when the resident host is unavailable and must never send text to it.
/// </summary>
public interface IRustShadowAuditStatusReader
{
    Task<RustShadowAuditSnapshot?> TryReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stable, local-only endpoint shared by the resident keyboard host and the
/// settings window. The endpoint transfers a single aggregate snapshot only.
/// </summary>
public static class RustShadowAuditIpc
{
    public const string PipeName = "SmartInput.RustShadowAudit.v1";
}

public sealed class RustShadowAuditStatus
{
    public int Observed { get; init; }
    public int ProviderUnavailable { get; init; }
    public int NativeFailures { get; init; }
    public int BothKeep { get; init; }
    public int RustKeepsOwnApplies { get; init; }
    public int RustAppliesOwnKeeps { get; init; }
    public int MatchingApplies { get; init; }
    public int DifferingApplies { get; init; }
    public int OwnLayoutApplies { get; init; }
    public int OwnSpellingApplies { get; init; }
    public int OwnCombinedApplies { get; init; }
    public int OwnWaits { get; init; }
    public int OwnKeeps { get; init; }
    public int RustLayoutReplacements { get; init; }
    public int RustSpellingReplacements { get; init; }
    public int RustLearnedReplacements { get; init; }
    public int RustProtectedKeeps { get; init; }
    public int RustLowConfidenceKeeps { get; init; }
    public int CandidatesForHumanReview { get; init; }
    public long TotalEvaluationMicroseconds { get; init; }
    public long MaxEvaluationMicroseconds { get; init; }
}

/// <summary>
/// Runs Rust only after the existing host pipeline has already permitted a
/// token for analysis. It records aggregate-only comparison counters and has
/// no reference to text replacement, Undo, policy changes or settings writes.
/// </summary>
public sealed class RustShadowAuditService : IRustShadowAuditService
{
    private readonly IRustShadowCandidateProvider _provider;
    private readonly object _sync = new();

    private int _observed;
    private int _providerUnavailable;
    private int _nativeFailures;
    private int _bothKeep;
    private int _rustKeepsOwnApplies;
    private int _rustAppliesOwnKeeps;
    private int _matchingApplies;
    private int _differingApplies;
    private int _ownLayoutApplies;
    private int _ownSpellingApplies;
    private int _ownCombinedApplies;
    private int _ownWaits;
    private int _ownKeeps;
    private int _rustLayoutReplacements;
    private int _rustSpellingReplacements;
    private int _rustLearnedReplacements;
    private int _rustProtectedKeeps;
    private int _rustLowConfidenceKeeps;
    private int _candidatesForHumanReview;
    private long _totalEvaluationMicroseconds;
    private long _maxEvaluationMicroseconds;

    public RustShadowAuditService(IRustShadowCandidateProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public RustShadowProviderState ProviderState => _provider.State;

    public RustShadowAuditStatus Status
    {
        get
        {
            lock (_sync)
            {
                return new RustShadowAuditStatus
                {
                    Observed = _observed,
                    ProviderUnavailable = _providerUnavailable,
                    NativeFailures = _nativeFailures,
                    BothKeep = _bothKeep,
                    RustKeepsOwnApplies = _rustKeepsOwnApplies,
                    RustAppliesOwnKeeps = _rustAppliesOwnKeeps,
                    MatchingApplies = _matchingApplies,
                    DifferingApplies = _differingApplies,
                    OwnLayoutApplies = _ownLayoutApplies,
                    OwnSpellingApplies = _ownSpellingApplies,
                    OwnCombinedApplies = _ownCombinedApplies,
                    OwnWaits = _ownWaits,
                    OwnKeeps = _ownKeeps,
                    RustLayoutReplacements = _rustLayoutReplacements,
                    RustSpellingReplacements = _rustSpellingReplacements,
                    RustLearnedReplacements = _rustLearnedReplacements,
                    RustProtectedKeeps = _rustProtectedKeeps,
                    RustLowConfidenceKeeps = _rustLowConfidenceKeeps,
                    CandidatesForHumanReview = _candidatesForHumanReview,
                    TotalEvaluationMicroseconds = _totalEvaluationMicroseconds,
                    MaxEvaluationMicroseconds = _maxEvaluationMicroseconds,
                };
            }
        }
    }

    public void Observe(string token, string context, JointCorrectionDecisionResult ownDecision)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ownDecision);

        var startedAt = Stopwatch.GetTimestamp();
        RustShadowCandidateResult rust;
        try
        {
            rust = _provider.Evaluate(token, context);
        }
        catch
        {
            // A shadow provider must never alter the live keyboard path.
            rust = RustShadowCandidateResult.Unavailable(RustShadowProviderState.NativeFailure);
        }

        var elapsedMicroseconds = ToMicroseconds(Stopwatch.GetElapsedTime(startedAt).Ticks);

        var ownApplies = ownDecision.Recommendation == JointCorrectionRecommendation.Apply
            && !string.IsNullOrEmpty(ownDecision.ReplacementToken);
        var outcome = DetermineOutcome(rust, ownDecision, ownApplies);
        var promotion = DeterminePromotionDisposition(rust, ownDecision, ownApplies);

        lock (_sync)
        {
            _observed++;
            _totalEvaluationMicroseconds += elapsedMicroseconds;
            _maxEvaluationMicroseconds = Math.Max(_maxEvaluationMicroseconds, elapsedMicroseconds);
            RecordOwnDecision(ownDecision);
            RecordRustDecision(rust);
            if (promotion == RustShadowPromotionDisposition.CandidateForHumanReview)
            {
                _candidatesForHumanReview++;
            }

            switch (outcome)
            {
                case RustShadowComparisonOutcome.ProviderUnavailable:
                    _providerUnavailable++;
                    break;
                case RustShadowComparisonOutcome.NativeFailure:
                    _nativeFailures++;
                    break;
                case RustShadowComparisonOutcome.BothKeep:
                    _bothKeep++;
                    break;
                case RustShadowComparisonOutcome.RustKeepsOwnApplies:
                    _rustKeepsOwnApplies++;
                    break;
                case RustShadowComparisonOutcome.RustAppliesOwnKeeps:
                    _rustAppliesOwnKeeps++;
                    break;
                case RustShadowComparisonOutcome.RustMatchesOwnApply:
                    _matchingApplies++;
                    break;
                case RustShadowComparisonOutcome.RustDiffersFromOwnApply:
                    _differingApplies++;
                    break;
            }
        }
    }

    public RustShadowComparisonResult CompareForTesting(
        string token,
        string context,
        JointCorrectionDecisionResult ownDecision)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ownDecision);

        var rust = _provider.Evaluate(token, context);
        var ownApplies = ownDecision.Recommendation == JointCorrectionRecommendation.Apply
            && !string.IsNullOrEmpty(ownDecision.ReplacementToken);
        var outcome = DetermineOutcome(rust, ownDecision, ownApplies);

        return new RustShadowComparisonResult(
            outcome,
            rust.ProviderState,
            rust.Reason,
            rust.Confidence,
            rust.Margin,
            ownDecision.Recommendation.ToString(),
            ownDecision.Kind.ToString(),
            DigestOrNull(ownDecision.ReplacementToken),
            DigestOrNull(rust.ReplacementToken));
    }

    private static RustShadowComparisonOutcome DetermineOutcome(
        RustShadowCandidateResult rust,
        JointCorrectionDecisionResult ownDecision,
        bool ownApplies)
    {
        if (rust.ProviderState != RustShadowProviderState.Available)
        {
            return rust.ProviderState == RustShadowProviderState.NativeFailure
                ? RustShadowComparisonOutcome.NativeFailure
                : RustShadowComparisonOutcome.ProviderUnavailable;
        }

        if (rust.Decision != RustShadowDecision.Replace || string.IsNullOrEmpty(rust.ReplacementToken))
        {
            return ownApplies
                ? RustShadowComparisonOutcome.RustKeepsOwnApplies
                : RustShadowComparisonOutcome.BothKeep;
        }

        if (!ownApplies)
        {
            return RustShadowComparisonOutcome.RustAppliesOwnKeeps;
        }

        return string.Equals(rust.ReplacementToken, ownDecision.ReplacementToken, StringComparison.Ordinal)
            ? RustShadowComparisonOutcome.RustMatchesOwnApply
            : RustShadowComparisonOutcome.RustDiffersFromOwnApply;
    }

    /// <summary>
    /// Strictly classifies only exact, high-confidence agreement as something
    /// that a human may later inspect. It never changes an outcome and is not
    /// connected to text replacement or learning.
    /// </summary>
    public static RustShadowPromotionDisposition DeterminePromotionDisposition(
        RustShadowCandidateResult rust,
        JointCorrectionDecisionResult ownDecision,
        bool ownApplies)
    {
        if (!ownApplies
            || rust.ProviderState != RustShadowProviderState.Available
            || rust.Decision != RustShadowDecision.Replace
            || string.IsNullOrEmpty(rust.ReplacementToken)
            || !string.Equals(rust.ReplacementToken, ownDecision.ReplacementToken, StringComparison.Ordinal)
            || rust.Confidence < 0.98
            || rust.Margin < 0.20)
        {
            return RustShadowPromotionDisposition.NotEligible;
        }

        var sameDecisionKind = ownDecision.Kind switch
        {
            CorrectionKind.Layout => rust.Reason == RustShadowReason.Layout,
            CorrectionKind.Autocorrect => rust.Reason == RustShadowReason.Spelling,
            _ => false,
        };

        return sameDecisionKind
            ? RustShadowPromotionDisposition.CandidateForHumanReview
            : RustShadowPromotionDisposition.NotEligible;
    }

    private void RecordOwnDecision(JointCorrectionDecisionResult decision)
    {
        if (decision.Recommendation == JointCorrectionRecommendation.Wait)
        {
            _ownWaits++;
            return;
        }

        if (decision.Recommendation != JointCorrectionRecommendation.Apply)
        {
            _ownKeeps++;
            return;
        }

        switch (decision.Kind)
        {
            case CorrectionKind.Layout:
                _ownLayoutApplies++;
                break;
            case CorrectionKind.Autocorrect:
                _ownSpellingApplies++;
                break;
            case CorrectionKind.Combined:
                _ownCombinedApplies++;
                break;
            default:
                _ownKeeps++;
                break;
        }
    }

    private void RecordRustDecision(RustShadowCandidateResult rust)
    {
        if (rust.Decision == RustShadowDecision.Replace)
        {
            switch (rust.Reason)
            {
                case RustShadowReason.Layout:
                    _rustLayoutReplacements++;
                    break;
                case RustShadowReason.Spelling:
                    _rustSpellingReplacements++;
                    break;
                case RustShadowReason.LearnedCorrection:
                    _rustLearnedReplacements++;
                    break;
            }

            return;
        }

        switch (rust.Reason)
        {
            case RustShadowReason.ProtectedContext:
            case RustShadowReason.ProtectedToken:
                _rustProtectedKeeps++;
                break;
            case RustShadowReason.LowConfidence:
                _rustLowConfidenceKeeps++;
                break;
        }
    }

    private static long ToMicroseconds(long timeSpanTicks) =>
        checked((long)Math.Round(timeSpanTicks * (1_000_000.0 / TimeSpan.TicksPerSecond)));

    private static string? DigestOrNull(string? text) =>
        string.IsNullOrEmpty(text)
            ? null
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
