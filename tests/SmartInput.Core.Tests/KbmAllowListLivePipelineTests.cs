using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public sealed class KbmAllowListLivePipelineTests
{
    [Fact]
    public async Task ConfirmedKbmRelation_UsesTheExistingReplacementAndUndoPath()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var provider = new FixedProvider(KbmMarkerRoute.Prefix02, "kommersant");
        var allowList = new KbmAllowListCorrectionService(provider);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            kbmAllowList: allowList);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "commersant");
        await engine.ProcessInputAsync(
            TokenInputEvent.Boundary(LiveCorrectionTestHelpers.SpaceBoundaryKey()));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("commersant", replacement.LastOriginal);
        Assert.Equal("kommersant", replacement.LastReplacement);
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
                    8184,
                    _text,
                    "000000",
                    input.Route,
                    "test");
                return true;
            }

            candidate = null;
            return false;
        }
    }
}
