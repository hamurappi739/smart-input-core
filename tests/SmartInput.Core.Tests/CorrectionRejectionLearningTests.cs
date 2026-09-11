using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public class CorrectionRejectionPolicyTests
{
    [Fact]
    public async Task IsAutomaticallySuppressed_BelowThreshold_ReturnsFalse()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 1,
        });

        var policy = new CorrectionRejectionPolicy(store);

        Assert.False(policy.IsAutomaticallySuppressed("ghbdtn", "привет", CorrectionKind.Layout));
    }

    [Fact]
    public async Task IsAutomaticallySuppressed_AtThreshold_ReturnsTrue()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });

        var policy = new CorrectionRejectionPolicy(store);

        Assert.True(policy.IsAutomaticallySuppressed("ghbdtn", "привет", CorrectionKind.Layout));
    }

    [Fact]
    public async Task IsAutomaticallySuppressed_DifferentKind_ReturnsFalse()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });

        var policy = new CorrectionRejectionPolicy(store);

        Assert.False(policy.IsAutomaticallySuppressed("ghbdtn", "привет", CorrectionKind.Autocorrect));
    }

    [Fact]
    public async Task IsAutomaticallySuppressed_DifferentPair_ReturnsFalse()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });

        var policy = new CorrectionRejectionPolicy(store);

        Assert.False(policy.IsAutomaticallySuppressed("ghbdtn", "hello", CorrectionKind.Layout));
    }

    [Fact]
    public async Task AllowAgainAsync_RemovesSuppression()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "helo",
            Replacement = "hello",
            Kind = CorrectionKind.Autocorrect,
            UndoCount = 2,
        });

        var policy = new CorrectionRejectionPolicy(store);
        Assert.True(policy.IsAutomaticallySuppressed("helo", "hello", CorrectionKind.Autocorrect));

        await policy.AllowAgainAsync("helo", "hello", CorrectionKind.Autocorrect);

        Assert.False(policy.IsAutomaticallySuppressed("helo", "hello", CorrectionKind.Autocorrect));
    }
}

public class CorrectionRejectionLearningEngineTests
{
    [Fact]
    public async Task ProcessInputAsync_AfterOneRejection_StillCorrectsLayout()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 1,
        });

        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            rejectionPolicy: new CorrectionRejectionPolicy(store));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(LiveLayoutCorrectionAction.CorrectionSucceeded, engine.Status.LastAction);
    }

    [Fact]
    public async Task ProcessInputAsync_AfterTwoRejections_SkipsLayoutCorrection()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });

        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            rejectionPolicy: new CorrectionRejectionPolicy(store));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, engine.Status.CorrectionsSucceeded);
        Assert.True(engine.Status.CorrectionsBlocked > 0);
    }

    [Fact]
    public async Task ProcessInputAsync_AfterTwoRejections_NotifiesOnManualPathViaSeparateService()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });

        var manualService = new ManualLayoutConversionService(
            new KeyboardLayoutConverter(),
            new NoSelectionSelectedTextService("ghbdtn"),
            new LiveCorrectionTestHelpers.FakeAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy()));

        var result = await manualService.ConvertSelectedTextAsync(LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(LayoutConversionStatus.Success, result.Status);
    }

    [Fact]
    public async Task ProcessInputAsync_SuccessfulCorrection_NotifiesFeedback()
    {
        var notifier = new RecordingCorrectionFeedbackNotifier();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            feedbackNotifier: notifier);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(CorrectionKind.Layout, notifier.LastSuccessfulKind);
        Assert.Equal(1, notifier.SuccessfulCorrectionCount);
    }

    internal sealed class RecordingCorrectionFeedbackNotifier : ICorrectionFeedbackNotifier
    {
        public int SuccessfulCorrectionCount { get; private set; }

        public CorrectionKind? LastSuccessfulKind { get; private set; }

        public int UndoneCount { get; private set; }

        public int UserInputCount { get; private set; }

        public int ContextInvalidatedCount { get; private set; }

        public void NotifySuccessfulCorrection(CorrectionKind kind)
        {
            SuccessfulCorrectionCount++;
            LastSuccessfulKind = kind;
        }

        public void NotifyCorrectionUndone() => UndoneCount++;

        public void NotifyUserInput() => UserInputCount++;

        public void NotifyContextInvalidated() => ContextInvalidatedCount++;
    }

    private sealed class NoSelectionSelectedTextService(string selectedText)
        : Platform.Abstractions.Text.ISelectedTextService
    {
        public Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(selectedText);

        public Task<bool> ReplaceSelectedTextAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
