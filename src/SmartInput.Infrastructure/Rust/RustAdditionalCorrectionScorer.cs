using System.Runtime.InteropServices;
using SmartInput.Core.Integration;

namespace SmartInput.Infrastructure.Rust;

/// <summary>
/// Adapts the existing optional Rust provider to the generic scorer contract.
/// The scorer itself never performs replacement; the separate hybrid bridge
/// may provide a bounded candidate to C# when its explicit allow-list mode is
/// enabled.
/// </summary>
public sealed class RustAdditionalCorrectionScorer : IAdditionalCorrectionScorer
{
    private readonly IRustShadowCandidateProvider _provider;

    public RustAdditionalCorrectionScorer(IRustShadowCandidateProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public AdditionalCorrectionScore Evaluate(PortableCorrectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        RustShadowCandidateResult result;
        try
        {
            // The portable contract intentionally contains only aggregate
            // context. Never reconstruct or forward raw previous words here.
            result = _provider.Evaluate(
                request.Token,
                RustShadowContextFormatter.FromPortableRequest(request));
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or InvalidOperationException
            or ObjectDisposedException
            or SEHException)
        {
            return new AdditionalCorrectionScore("wait", null, 0, "provider_failure", "provider_boundary_failure");
        }

        var confidence = Clamp(result.Confidence);
        var kind = MapKind(result.Reason);
        if (result.ProviderState is not RustShadowProviderState.Available)
        {
            return new AdditionalCorrectionScore("wait", null, confidence, "provider_unavailable", result.ProviderState.ToString());
        }

        if (result.Decision != RustShadowDecision.Replace
            || string.IsNullOrWhiteSpace(result.ReplacementToken))
        {
            return new AdditionalCorrectionScore(
                result.Reason == RustShadowReason.LowConfidence ? "wait" : "no_change",
                null,
                confidence,
                kind,
                result.Reason.ToString());
        }

        return new AdditionalCorrectionScore(
            "apply",
            result.ReplacementToken,
            confidence,
            kind,
            result.Reason.ToString());
    }

    private static string MapKind(RustShadowReason reason) => reason switch
    {
        RustShadowReason.Layout => "layout",
        RustShadowReason.Spelling or RustShadowReason.LearnedCorrection => "spelling",
        RustShadowReason.ProtectedContext or RustShadowReason.ProtectedToken => "protected",
        _ => "unknown",
    };

    private static double Clamp(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

}
