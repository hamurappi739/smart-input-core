using System.Text;

namespace SmartInput.Core.Integration;

/// <summary>
/// Marker route is part of the recovered KBM key. It must be selected by the
/// caller's boundary/preparation policy; a provider must never try all routes
/// as a fuzzy fallback.
/// </summary>
public enum KbmMarkerRoute
{
    Raw,
    Prefix02,
    Suffix03,
    Wrapped0203,
}

/// <summary>Already prepared bytes for an exact KBM lookup.</summary>
public sealed record KbmPreparedInput
{
    private const int MaximumPreparedBytes = 4096;

    private KbmPreparedInput(byte[] bytes, KbmMarkerRoute route)
    {
        Bytes = bytes;
        Route = route;
    }

    public ReadOnlyMemory<byte> Bytes { get; }
    public KbmMarkerRoute Route { get; }

    public static KbmPreparedInput FromPreparedBytes(
        ReadOnlySpan<byte> preparedBytes,
        KbmMarkerRoute route)
    {
        if (preparedBytes.IsEmpty)
        {
            throw new ArgumentException("Prepared input cannot be empty.", nameof(preparedBytes));
        }

        if (preparedBytes.Length > MaximumPreparedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(preparedBytes), "Prepared input is too large.");
        }

        return new KbmPreparedInput(preparedBytes.ToArray(), route);
    }

    public static KbmPreparedInput FromRawBytes(
        ReadOnlySpan<byte> tokenBytes,
        KbmMarkerRoute route)
    {
        if (tokenBytes.IsEmpty)
        {
            throw new ArgumentException("Prepared input cannot be empty.", nameof(tokenBytes));
        }

        var markerPrefix = route is KbmMarkerRoute.Prefix02 or KbmMarkerRoute.Wrapped0203;
        var markerSuffix = route is KbmMarkerRoute.Suffix03 or KbmMarkerRoute.Wrapped0203;
        var length = checked(tokenBytes.Length + (markerPrefix ? 1 : 0) + (markerSuffix ? 1 : 0));
        if (length > MaximumPreparedBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenBytes), "Prepared input is too large.");
        }

        var result = new byte[length];
        var offset = 0;
        if (markerPrefix)
        {
            result[offset++] = 0x02;
        }

        tokenBytes.CopyTo(result.AsSpan(offset));
        offset += tokenBytes.Length;
        if (markerSuffix)
        {
            result[offset] = 0x03;
        }

        return FromPreparedBytes(result, route);
    }

    public static KbmPreparedInput FromToken(string token, KbmMarkerRoute route)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("Token cannot be empty.", nameof(token));
        }

        return FromRawBytes(Encoding.UTF8.GetBytes(token), route);
    }
}

/// <summary>
/// Candidate metadata is intentionally opaque. The three metadata bytes are
/// preserved as hex and are not given semantic names until independently
/// proven.
/// </summary>
public sealed record KbmCandidate(
    int OutputId,
    string Text,
    string MetadataHex,
    KbmMarkerRoute Route,
    string ModelSha256);

/// <summary>
/// Exact, audit/preview-only candidate provider. It returns observations and
/// has no method capable of applying text or invoking an input API.
/// </summary>
public interface IKbmCandidateProvider
{
    bool TryResolve(KbmPreparedInput input, out KbmCandidate? candidate);
}

/// <summary>Safe default when no recovered KBM model is configured.</summary>
public sealed class NullKbmCandidateProvider : IKbmCandidateProvider
{
    public bool TryResolve(KbmPreparedInput input, out KbmCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(input);
        candidate = null;
        return false;
    }
}
