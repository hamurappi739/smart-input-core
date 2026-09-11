using SmartInput.Core.Integration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public sealed class RustHybridLivePipelineTests
{
    [Fact]
    public async Task EnabledRustCandidate_UsesTheExistingReplacementAndUndoPath()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var undo = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var hybrid = new FixedHybridService(new JointCorrectionDecisionResult
        {
            OriginalToken = "qzjxv",
            ReplacementToken = "correct",
            Recommendation = JointCorrectionRecommendation.Apply,
            Kind = CorrectionKind.Autocorrect,
            ConfidenceScore = 0.99,
        });
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            undoService: undo,
            rustHybrid: hybrid);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "qzjxv");
        await engine.ProcessInputAsync(
            TokenInputEvent.Boundary(LiveCorrectionTestHelpers.SpaceBoundaryKey()));

        Assert.Equal(1, hybrid.CallCount);
        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("qzjxv", replacement.LastOriginal);
        Assert.Equal("correct", replacement.LastReplacement);
        Assert.Equal(1, undo.RecordCount);
        Assert.Equal(CorrectionKind.Autocorrect, undo.LastRecordedTransaction?.Kind);
    }

    [Fact]
    public async Task DisabledRustLiveMode_DoesNotReachReplacementPath()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var hybrid = new FixedHybridService(
            new JointCorrectionDecisionResult
            {
                OriginalToken = "qzjxv",
                ReplacementToken = "correct",
                Recommendation = JointCorrectionRecommendation.Apply,
                Kind = CorrectionKind.Autocorrect,
                ConfidenceScore = 0.99,
            },
            liveEnabled: false);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            rustHybrid: hybrid);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "qzjxv");
        await engine.ProcessInputAsync(
            TokenInputEvent.Boundary(LiveCorrectionTestHelpers.SpaceBoundaryKey()));

        Assert.Equal(0, hybrid.CallCount);
        Assert.Equal(0, replacement.CallCount);
    }

    private sealed class FixedHybridService(
        JointCorrectionDecisionResult decision,
        bool liveEnabled = true) : IRustHybridCorrectionService
    {
        public bool IsLiveEnabled => liveEnabled;

        public int CallCount { get; private set; }

        public JointCorrectionDecisionResult? TryCreateApprovedDecision(
            string token,
            JointCorrectionDecisionResult coreDecision,
            SmartInput.Core.Dictionaries.IAutocorrectDictionary dictionary,
            bool layoutEnabled,
            bool autocorrectEnabled,
            SmartInput.Core.Configuration.AutocorrectionOptions options,
            SentenceLanguageHint languageHint)
        {
            _ = token;
            _ = coreDecision;
            _ = dictionary;
            _ = layoutEnabled;
            _ = autocorrectEnabled;
            _ = options;
            _ = languageHint;
            CallCount++;
            return decision;
        }
    }
}
