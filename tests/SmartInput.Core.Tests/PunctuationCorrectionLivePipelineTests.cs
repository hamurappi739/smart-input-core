using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class PunctuationCorrectionLivePipelineTests
{
    [Fact]
    public async Task ProcessInputAsync_RecordsPunctuationUndoTransaction()
    {
        var undoService = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement,
            delivery,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", ',');

        Assert.Equal(CorrectionKind.Punctuation, undoService.LastRecordedTransaction?.Kind);
        Assert.Equal(" ", undoService.LastRecordedTransaction?.OriginalToken);
        Assert.Equal(string.Empty, undoService.LastRecordedTransaction?.ReplacementToken);
        Assert.Equal(",", undoService.LastRecordedTransaction?.TrailingText);
        Assert.Equal(1, undoService.RecordCount);
    }

    [Fact]
    public async Task TryUndoAsync_AfterPunctuationCorrection_RestoresOriginalSpacing()
    {
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(replacement, LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(new CorrectionTransaction
        {
            OriginalToken = " ",
            ReplacementToken = string.Empty,
            TrailingText = ",",
            Kind = CorrectionKind.Punctuation,
        });

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.Success, result.Outcome);
        Assert.Equal(CorrectionKind.Punctuation, result.Kind);
        Assert.Equal(",", replacement.LastOriginal);
        Assert.Equal(" ,", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_SecondCorrection_SupersedesFirstUndo()
    {
        var undoService = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement,
            delivery,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Раз", ',');
        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, " ");
        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Два", '?');

        Assert.Equal(2, undoService.RecordCount);
        Assert.Equal(CorrectionKind.Punctuation, undoService.LastRecordedTransaction?.Kind);
        Assert.Equal("?", undoService.LastRecordedTransaction?.TrailingText);
    }

    [Fact]
    public async Task NotifyApplicationContextChanged_InvalidatesPunctuationUndo()
    {
        var undoService = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var applicationContext = new CorrectionApplicationContext();
        applicationContext.Update("notepad", 100);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement,
            applicationContext: applicationContext,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", '!');
        Assert.True(undoService.IsUndoAvailable);

        engine.NotifyApplicationContextChanged();
        undoService.NotifyApplicationContextChanged("word", 200);

        Assert.False(undoService.IsUndoAvailable);
    }

    [Fact]
    public async Task ProcessInputAsync_ConcurrentInputAbortsSafely_StillDeliversBoundaryOnce()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(
            (_, _) => TextReplacementResult.AbortedByUserInput(1, 0));
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement,
            delivery);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", ',');

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(2, delivery.DeliverCallCount);
        Assert.Equal(LiveLayoutCorrectionAction.PunctuationFailed, engine.Status.LastAction);
        Assert.Equal(0, engine.Status.PunctuationSucceeded);
    }

    [Fact]
    public async Task ProcessInputAsync_DeliversPunctuationBoundaryExactlyOnce()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement,
            delivery);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Как дела", '?');

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(2, delivery.DeliverCallCount);
        Assert.Equal(1, engine.Status.PunctuationSucceeded);
        Assert.Equal(2, engine.Status.BoundariesDelivered);
    }

    [Fact]
    public async Task LiveLayoutBoundaryGate_PunctuationCandidate_SuppressesBoundary()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement);

        var gate = new LiveLayoutBoundaryGate(
            engine,
            new LiveCorrectionTestHelpers.FakeSettingsService(LiveCorrectionTestHelpers.EnabledPunctuationSettings()),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false));

        foreach (var character in "Привет")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
            await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
        }

        await engine.ProcessInputAsync(TokenInputEvent.Boundary(LiveCorrectionTestHelpers.SpaceBoundaryKey()));
        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(LiveCorrectionTestHelpers.SpaceBoundaryKey())));

        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(LiveCorrectionTestHelpers.PunctuationBoundaryKey(','))));
        Assert.True(gate.ShouldSuppressPendingBoundary());
    }

    private sealed class FakeEmergencyPauseService(bool isPaused) : Platform.Abstractions.Safety.IEmergencyPauseService
    {
        public bool IsPaused => isPaused;

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }

    private sealed class FakeReplacementSessionNotifier(bool isActive) : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => isActive;

        public void NotifyKeyboardEventDuringReplacement(
            SmartInput.Platform.Abstractions.Input.KeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }

    private static CorrectionUndoService CreateUndoService(
        ISafeTextReplacementService replacement,
        AutomationPolicyResult policy,
        CorrectionUndoOptions? options = null)
    {
        return new CorrectionUndoService(
            replacement,
            new LiveCorrectionTestHelpers.FakeAutomationSafetyService(policy),
            new LiveCorrectionTestHelpers.FakeSettingsService(LiveCorrectionTestHelpers.EnabledPunctuationSettings()),
            new EmptyLearningStore(),
            options);
    }

    private sealed class TrackingReplacementService : ISafeTextReplacementService
    {
        public int CallCount { get; private set; }

        public string? LastOriginal { get; private set; }

        public string? LastReplacement { get; private set; }

        public Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOriginal = originalText;
            LastReplacement = replacementText;
            return Task.FromResult(TextReplacementResult.Success(originalText.Length, replacementText.Length));
        }

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync("abc", "xyz", cancellationToken);

        public Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
    }

    private sealed class EmptyLearningStore : ICorrectionRejectionLearningStore
    {
        public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public int GetUndoCount(string candidate, string replacement, CorrectionKind kind) => 0;

        public Task RecordRejectionAsync(
            CorrectionRejectionLearningEntry entry,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CorrectionRejectionLearningEntry>> GetEntriesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CorrectionRejectionLearningEntry>>([]);

        public Task RemoveAsync(
            string candidate,
            string replacement,
            CorrectionKind kind,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
