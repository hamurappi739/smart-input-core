namespace SmartInput.Core.Integration;

/// <summary>
/// A deliberately tiny bridge for relations independently confirmed in the
/// recovered KBM artifact. It is an authorization list, not a dictionary:
/// every entry requires an exact source, an exact marker route and an exact
/// candidate text from the verified model.
/// </summary>
public interface IKbmAllowListCorrectionService
{
    bool TryGetApprovedCandidate(string sourceToken, out KbmCandidate? candidate);
}

public sealed class KbmAllowListCorrectionService : IKbmAllowListCorrectionService
{
    private static readonly AllowListEntry[] Entries =
    [
        new("commersant", "kommersant", KbmMarkerRoute.Prefix02),
        new("infact", "in fact", KbmMarkerRoute.Wrapped0203),
        new("Аксенов", "Аксёнов", KbmMarkerRoute.Prefix02),
    ];

    private readonly IKbmCandidateProvider _provider;

    public KbmAllowListCorrectionService(IKbmCandidateProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public bool TryGetApprovedCandidate(string sourceToken, out KbmCandidate? candidate)
    {
        candidate = null;
        if (string.IsNullOrWhiteSpace(sourceToken))
        {
            return false;
        }

        foreach (var entry in Entries)
        {
            if (!string.Equals(entry.SourceToken, sourceToken, StringComparison.Ordinal))
            {
                continue;
            }

            KbmPreparedInput prepared;
            try
            {
                prepared = KbmPreparedInput.FromToken(sourceToken, entry.Route);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (_provider.TryResolve(prepared, out var resolved)
                && resolved is not null
                && string.Equals(resolved.Text, entry.ReplacementToken, StringComparison.Ordinal)
                && !string.Equals(resolved.Text, sourceToken, StringComparison.Ordinal))
            {
                candidate = resolved;
                return true;
            }

            return false;
        }

        return false;
    }

    private sealed record AllowListEntry(
        string SourceToken,
        string ReplacementToken,
        KbmMarkerRoute Route);
}
