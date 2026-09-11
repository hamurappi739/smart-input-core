using System.Security.Cryptography;
using System.Text;

namespace SmartInput.Core.Integration;

public enum KbmAuditComparisonOutcome
{
    NoCandidate,
    CandidateAvailable,
    CandidateMatchesOwnApply,
    CandidateDiffersFromOwnApply,
    OwnEngineWaits,
    OwnEngineNoChange,
}

/// <summary>
/// Privacy-safe, in-memory comparison between the recovered KBM provider and
/// the current SmartInput decision engine. It exposes digests rather than
/// candidate text so callers can aggregate results without logging content.
/// </summary>
public sealed record KbmAuditComparisonResult(
    KbmAuditComparisonOutcome Outcome,
    string OwnDecision,
    string OwnKind,
    string? OwnReplacementDigest,
    string? KbmCandidateDigest,
    int? KbmOutputId,
    string? KbmModelSha256,
    KbmMarkerRoute Route);

public interface IKbmAuditComparisonService
{
    KbmAuditComparisonResult Compare(
        PortableCorrectionRequest request,
        KbmPreparedInput preparedInput,
        IKbmCandidateProvider provider);
}

public sealed class KbmAuditComparisonService : IKbmAuditComparisonService
{
    private readonly IPortableCorrectionEngine _ownEngine;

    public KbmAuditComparisonService(IPortableCorrectionEngine ownEngine)
    {
        _ownEngine = ownEngine ?? throw new ArgumentNullException(nameof(ownEngine));
    }

    public KbmAuditComparisonResult Compare(
        PortableCorrectionRequest request,
        KbmPreparedInput preparedInput,
        IKbmCandidateProvider provider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preparedInput);
        ArgumentNullException.ThrowIfNull(provider);

        var own = _ownEngine.Evaluate(request);
        var hasCandidate = provider.TryResolve(preparedInput, out var candidate);
        if (!hasCandidate || candidate is null)
        {
            return new KbmAuditComparisonResult(
                KbmAuditComparisonOutcome.NoCandidate,
                own.Decision,
                own.Kind,
                DigestOrNull(own.ReplacementToken),
                null,
                null,
                null,
                preparedInput.Route);
        }

        var candidateDigest = Digest(candidate.Text);
        var ownDigest = DigestOrNull(own.ReplacementToken);
        var outcome = own.Decision switch
        {
            "apply" when string.Equals(own.ReplacementToken, candidate.Text, StringComparison.Ordinal)
                => KbmAuditComparisonOutcome.CandidateMatchesOwnApply,
            "apply" => KbmAuditComparisonOutcome.CandidateDiffersFromOwnApply,
            "wait" => KbmAuditComparisonOutcome.OwnEngineWaits,
            _ => KbmAuditComparisonOutcome.OwnEngineNoChange,
        };

        return new KbmAuditComparisonResult(
            outcome,
            own.Decision,
            own.Kind,
            ownDigest,
            candidateDigest,
            candidate.OutputId,
            candidate.ModelSha256,
            preparedInput.Route);
    }

    private static string? DigestOrNull(string? value)
        => string.IsNullOrEmpty(value) ? null : Digest(value);

    private static string Digest(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
