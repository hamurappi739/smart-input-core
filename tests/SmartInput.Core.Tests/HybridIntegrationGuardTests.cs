using System.Security.Cryptography;
using System.Text;
using SmartInput.Core.Integration;

namespace SmartInput.Core.Tests;

public sealed class HybridIntegrationGuardTests
{
    [Fact]
    public void Comparison_ReportsMatchUsingDigestsOnly()
    {
        var own = new RecordingPortableEngine(new PortableCorrectionResponse
        {
            OriginalToken = "helo",
            ReplacementToken = "hello",
            Decision = "apply",
            Kind = "autocorrect",
            ConfidenceScore = 0.95,
            Reason = "candidate_passed_policy",
        });
        var provider = new FixedKbmProvider(new KbmCandidate(
            7, "hello", "010001", KbmMarkerRoute.Prefix02, "MODEL"));

        var result = new KbmAuditComparisonService(own).Compare(
            new PortableCorrectionRequest { Token = "helo" },
            KbmPreparedInput.FromToken("helo", KbmMarkerRoute.Prefix02),
            provider);

        Assert.Equal(KbmAuditComparisonOutcome.CandidateMatchesOwnApply, result.Outcome);
        Assert.Equal(7, result.KbmOutputId);
        Assert.Equal("MODEL", result.KbmModelSha256);
        Assert.NotEqual("hello", result.KbmCandidateDigest);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hello"))),
            result.KbmCandidateDigest);
    }

    [Fact]
    public void Comparison_DistinguishesWaitAndNoCandidate()
    {
        var waiting = new RecordingPortableEngine(new PortableCorrectionResponse
        {
            OriginalToken = "helo",
            ReplacementToken = "hello",
            Decision = "wait",
            Kind = "autocorrect",
            ConfidenceScore = 0.71,
            Reason = "ambiguous",
        });
        var input = KbmPreparedInput.FromToken("helo", KbmMarkerRoute.Prefix02);
        var candidate = new FixedKbmProvider(new KbmCandidate(
            1, "hello", "000000", KbmMarkerRoute.Prefix02, "MODEL"));
        var service = new KbmAuditComparisonService(waiting);

        Assert.Equal(KbmAuditComparisonOutcome.OwnEngineWaits,
            service.Compare(new PortableCorrectionRequest { Token = "helo" }, input, candidate).Outcome);
        Assert.Equal(KbmAuditComparisonOutcome.NoCandidate,
            service.Compare(new PortableCorrectionRequest { Token = "helo" }, input, new FixedKbmProvider(null)).Outcome);
    }

    [Fact]
    public void OwnershipGate_AllowsOneOwnerAndRejectsCompetingGeneration()
    {
        var gate = new CorrectionOwnershipGate();
        Assert.True(gate.TryAcquire("smartinput", 1, out var lease));
        Assert.NotNull(lease);
        Assert.True(gate.CanApply(lease!));
        Assert.False(gate.TryAcquire("recovered-kbm", 1, out _));
        Assert.False(gate.TryAcquire("smartinput", 2, out _));

        gate.Release(lease);
        Assert.True(gate.TryAcquire("recovered-kbm", 1, out var second));
        Assert.True(gate.CanApply(second!));
    }

    [Theory]
    [InlineData(CorrectionEngineMode.Disabled)]
    [InlineData(CorrectionEngineMode.AuditOnly)]
    [InlineData(CorrectionEngineMode.Preview)]
    public void OwnershipGate_NonLiveModesCannotAcquire(CorrectionEngineMode mode)
    {
        var gate = new CorrectionOwnershipGate();
        gate.TrySetMode(mode);

        Assert.False(gate.TryAcquire("smartinput", 1, out var lease));
        Assert.Null(lease);
    }

    [Fact]
    public void OwnershipGate_ModeChangeInvalidatesExistingLease()
    {
        var gate = new CorrectionOwnershipGate();
        Assert.True(gate.TryAcquire("smartinput", 1, out var lease));

        gate.TrySetMode(CorrectionEngineMode.AuditOnly);
        Assert.False(gate.CanApply(lease!));
        Assert.Null(gate.ActiveOwner);

        gate.TrySetMode(CorrectionEngineMode.AllowList);
        Assert.True(gate.TryAcquire("recovered-kbm", 2, out var newLease));
        Assert.True(gate.CanApply(newLease!));
    }

    private sealed class FixedKbmProvider : IKbmCandidateProvider
    {
        private readonly KbmCandidate? _candidate;

        public FixedKbmProvider(KbmCandidate? candidate) => _candidate = candidate;

        public bool TryResolve(KbmPreparedInput input, out KbmCandidate? candidate)
        {
            candidate = _candidate;
            return candidate is not null;
        }
    }

    private sealed class RecordingPortableEngine : IPortableCorrectionEngine
    {
        private readonly PortableCorrectionResponse _response;

        public RecordingPortableEngine(PortableCorrectionResponse response) => _response = response;

        public PortableCorrectionResponse Evaluate(PortableCorrectionRequest request) => _response;
    }
}
