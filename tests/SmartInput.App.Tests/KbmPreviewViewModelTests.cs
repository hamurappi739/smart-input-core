using SmartInput.App.ViewModels;
using SmartInput.Core.Integration;

namespace SmartInput.App.Tests;

public sealed class KbmPreviewViewModelTests
{
    [Fact]
    public void Analyze_WithoutCandidate_ShowsSafeNoCandidateState()
    {
        var viewModel = new KbmPreviewViewModel(
            new NullKbmCandidateProvider(),
            new FakeComparisonService(new KbmAuditComparisonResult(
                KbmAuditComparisonOutcome.NoCandidate,
                "no_change", "none", null, null, null, null, KbmMarkerRoute.Prefix02)),
            new FakePortableEngine("no_change", null));

        viewModel.SourceToken = "commersant";
        viewModel.AnalyzeCommand.Execute(null);

        Assert.False(viewModel.HasCandidate);
        Assert.Equal("no_change", viewModel.OwnDecision);
        Assert.Equal("Кандидат не найден", viewModel.KbmResult);
        Assert.Contains("нет точного кандидата", viewModel.ComparisonStatus);
    }

    [Fact]
    public void Analyze_ShowsAgreementButHasNoExternalApplyCommand()
    {
        var viewModel = new KbmPreviewViewModel(
            new FixedKbmProvider(new KbmCandidate(
                8167, "kommersant", "000000", KbmMarkerRoute.Prefix02, "MODEL")),
            new FakeComparisonService(new KbmAuditComparisonResult(
                KbmAuditComparisonOutcome.CandidateMatchesOwnApply,
                "apply", "autocorrect", "OWNDIGEST", "KBMDIGEST", 8167, "MODEL", KbmMarkerRoute.Prefix02)),
            new FakePortableEngine("apply", "hello"));

        viewModel.SourceToken = "commersant";
        viewModel.AnalyzeCommand.Execute(null);

        Assert.True(viewModel.HasCandidate);
        Assert.Equal("apply", viewModel.OwnDecision);
        Assert.Contains("один результат", viewModel.ComparisonStatus);
        Assert.DoesNotContain("ApplyCommand", viewModel.GetType().GetMethods().Select(method => method.Name));
    }

    [Fact]
    public void Clear_RemovesAllPreviewState()
    {
        var viewModel = new KbmPreviewViewModel(
            new NullKbmCandidateProvider(),
            new FakeComparisonService(new KbmAuditComparisonResult(
                KbmAuditComparisonOutcome.CandidateAvailable,
                "wait", "autocorrect", null, "DIGEST", 1, "MODEL", KbmMarkerRoute.Raw)),
            new FakePortableEngine("wait", null));
        viewModel.SourceToken = "x";
        viewModel.AnalyzeCommand.Execute(null);

        viewModel.ClearCommand.Execute(null);

        Assert.Empty(viewModel.SourceToken);
        Assert.False(viewModel.HasCandidate);
        Assert.Equal("—", viewModel.OwnDecision);
    }

    private sealed class FakeComparisonService : IKbmAuditComparisonService
    {
        private readonly KbmAuditComparisonResult _result;

        public FakeComparisonService(KbmAuditComparisonResult result) => _result = result;

        public KbmAuditComparisonResult Compare(
            PortableCorrectionRequest request,
            KbmPreparedInput preparedInput,
            IKbmCandidateProvider provider) => _result;
    }

    private sealed class FixedKbmProvider : IKbmCandidateProvider
    {
        private readonly KbmCandidate _candidate;

        public FixedKbmProvider(KbmCandidate candidate) => _candidate = candidate;

        public bool TryResolve(KbmPreparedInput input, out KbmCandidate? candidate)
        {
            candidate = _candidate;
            return true;
        }
    }

    private sealed class FakePortableEngine : IPortableCorrectionEngine
    {
        private readonly string _decision;
        private readonly string? _replacement;

        public FakePortableEngine(string decision, string? replacement)
        {
            _decision = decision;
            _replacement = replacement;
        }

        public PortableCorrectionResponse Evaluate(PortableCorrectionRequest request)
            => new()
            {
                OriginalToken = request.Token,
                ReplacementToken = _replacement,
                Decision = _decision,
                Kind = "autocorrect",
                Reason = "test",
            };
    }
}
