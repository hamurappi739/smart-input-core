using SmartInput.Core.Integration;

namespace SmartInput.Core.Tests;

public sealed class KbmAllowListCorrectionServiceTests
{
    [Fact]
    public void ConfirmedRelation_IsAuthorizedOnlyForExactRouteAndText()
    {
        var provider = new FixedProvider(
            KbmMarkerRoute.Prefix02,
            "kommersant");
        var service = new KbmAllowListCorrectionService(provider);

        var allowed = service.TryGetApprovedCandidate("commersant", out var candidate);

        Assert.True(allowed);
        Assert.NotNull(candidate);
        Assert.Equal("kommersant", candidate!.Text);
        Assert.Equal(KbmMarkerRoute.Prefix02, candidate.Route);
    }

    [Fact]
    public void UnknownToken_IsNotAuthorized()
    {
        var service = new KbmAllowListCorrectionService(
            new FixedProvider(KbmMarkerRoute.Prefix02, "anything"));

        Assert.False(service.TryGetApprovedCandidate("unknown-token", out var candidate));
        Assert.Null(candidate);
    }

    [Fact]
    public void CandidateWithWrongText_IsRejected()
    {
        var service = new KbmAllowListCorrectionService(
            new FixedProvider(KbmMarkerRoute.Prefix02, "other"));

        Assert.False(service.TryGetApprovedCandidate("commersant", out var candidate));
        Assert.Null(candidate);
    }

    private sealed class FixedProvider : IKbmCandidateProvider
    {
        private readonly KbmMarkerRoute _route;
        private readonly string _text;

        public FixedProvider(KbmMarkerRoute route, string text)
        {
            _route = route;
            _text = text;
        }

        public bool TryResolve(KbmPreparedInput input, out KbmCandidate? candidate)
        {
            if (input.Route == _route)
            {
                candidate = new KbmCandidate(
                    OutputId: 1,
                    Text: _text,
                    MetadataHex: "000000",
                    Route: input.Route,
                    ModelSha256: "test");
                return true;
            }

            candidate = null;
            return false;
        }
    }
}
