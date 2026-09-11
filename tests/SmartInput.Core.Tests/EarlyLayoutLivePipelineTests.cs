using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.Core.Tests;

public sealed class EarlyLayoutLivePipelineTests
{
    private static readonly Lazy<EarlyLayoutModel> Model = new(() =>
        EarlyLayoutModel.Train(
            StarterAutocorrectLexicon.Entries.Select(pair =>
                (pair.Key.Language, pair.Key.Word, pair.Value * pair.Value))));

    [Fact]
    public async Task FourCharacterProposal_ReplacesPrefixOnce_AndKeepsContinuationInOrder()
    {
        var early = CreateEarlyService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var language = new RecordingLanguageService(success: true);
        var context = new CorrectionApplicationContext();
        context.Update("notepad", 42);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            applicationContext: context,
            keyboardInputLanguageService: language,
            earlyLayoutCorrectionService: early);
        engine.NotifyPolicyContextChanged(LiveCorrectionTestHelpers.AllowedPolicy());

        foreach (var character in "ghbd")
        {
            var prepared = early.ObserveCharacter(
                42,
                character,
                TypingLanguage.English,
                allowed: true);
            await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(
                character,
                prepared,
                prepared is not null));
        }

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("ghbd", replacement.LastOriginal);
        Assert.Equal("прив", replacement.LastReplacement);
        Assert.Equal(KeyboardInputLanguage.Russian, language.LastRequestedLanguage);
        Assert.Equal(4, engine.PendingTokenLength);

        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('е'));
        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('т'));
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(1, engine.Status.TokensCompleted);
        Assert.Equal(1, engine.Status.CorrectionsSucceeded);
    }

    [Theory]
    [InlineData("ghbd", "прив", TypingLanguage.English)]
    [InlineData("рудд", "hell", TypingLanguage.Russian)]
    public void DefaultFourCharacterModel_CoversBothLayoutDirections(
        string prefix,
        string replacement,
        TypingLanguage sourceLanguage)
    {
        var decision = Model.Value.Evaluate(prefix, sourceLanguage);
        Assert.True(
            decision.Verdict == EarlyLayoutVerdict.Candidate,
            $"verdict={decision.Verdict}; margin={decision.Margin}; length={decision.PrefixLength}");
        var session = new EarlyLayoutSession(Model.Value);
        session.SetContext(42, sourceLanguage, allowed: true);

        EarlyLayoutProposal? proposal = null;
        foreach (var character in prefix)
        {
            proposal = session.Append(character);
        }

        Assert.NotNull(proposal);
        Assert.Equal(prefix, proposal!.Original);
        Assert.Equal(replacement, proposal.Replacement);
    }

    [Fact]
    public async Task CompletedEarlyWord_RecordsFullUndoPairOnlyAtBoundary()
    {
        var early = CreateEarlyService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var undo = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var language = new RecordingLanguageService(success: true);
        var context = new CorrectionApplicationContext();
        context.Update("notepad", 42);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            applicationContext: context,
            undoService: undo,
            keyboardInputLanguageService: language,
            earlyLayoutCorrectionService: early);
        engine.NotifyPolicyContextChanged(LiveCorrectionTestHelpers.AllowedPolicy());

        await TypeThroughEarlySwitchAsync(engine, early, "ghbd");
        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('е'));
        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('т'));

        Assert.False(undo.IsUndoAvailable);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.True(undo.IsUndoAvailable);
        Assert.Equal(CorrectionKind.Layout, undo.LastRecordedTransaction?.Kind);
        Assert.Equal("ghbdtn", undo.LastRecordedTransaction?.OriginalToken);
        Assert.Equal("привет", undo.LastRecordedTransaction?.ReplacementToken);
    }

    [Theory]
    [InlineData("fron")]
    [InlineData("ghbd_name")]
    [InlineData("BMW")]
    public void OrdinaryOrProtectedPrefix_DoesNotArmEarlyCorrection(string token)
    {
        var early = CreateEarlyService();
        var prepared = (PreparedLayoutCorrection?)null;
        foreach (var character in token)
        {
            prepared = early.ObserveCharacter(
                42,
                character,
                TypingLanguage.English,
                allowed: true);
        }

        Assert.Null(prepared);
        Assert.False(early.HasPendingEarlyLayoutCorrection);
    }

    [Fact]
    public void HookPreflight_ArmsExactlyAtFourthCharacter_AndKeepsLaterKeysBehindBarrier()
    {
        var early = CreateEarlyService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var context = new CorrectionApplicationContext();
        context.Update("notepad", 42);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            applicationContext: context,
            keyboardInputLanguageService: new RecordingLanguageService(success: true),
            earlyLayoutCorrectionService: early);
        engine.NotifyPolicyContextChanged(LiveCorrectionTestHelpers.AllowedPolicy());

        var gate = new LiveLayoutBoundaryGate(
            engine,
            new LiveCorrectionTestHelpers.FakeSettingsService(
                LiveCorrectionTestHelpers.EnabledLayoutSettings()),
            new NoPauseService(),
            new NoReplacementSessionNotifier(),
            dictionary: LiveCorrectionTestHelpers.CreateStarterDictionary(),
            earlyLayoutCorrectionService: early);
        gate.ObserveApplicationContext(42);

        Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('g')));
        Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('h')));
        Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('b')));
        var prepared = gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('d'));

        Assert.NotNull(prepared);
        Assert.Equal("ghbd", prepared!.OriginalText);
        Assert.Equal("прив", prepared.ReplacementText);
        Assert.True(gate.HasPendingEarlyLayoutCorrection());

        Assert.True(early.TryReserve(prepared, 42, inputBarrierOwned: true));
        Assert.True(early.Complete(prepared, textApplied: true, layoutAcknowledged: true));
        Assert.False(gate.HasPendingEarlyLayoutCorrection());
    }

    [Fact]
    public async Task LayoutRequestFailure_RollsBackPrefixBeforeContinuing()
    {
        var early = CreateEarlyService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var language = new RecordingLanguageService(success: false);
        var context = new CorrectionApplicationContext();
        context.Update("notepad", 42);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            applicationContext: context,
            keyboardInputLanguageService: language,
            earlyLayoutCorrectionService: early);
        engine.NotifyPolicyContextChanged(LiveCorrectionTestHelpers.AllowedPolicy());

        await TypeThroughEarlySwitchAsync(engine, early, "ghbd");

        Assert.Equal(2, replacement.CallCount);
        Assert.Equal("прив", replacement.LastOriginal);
        Assert.Equal("ghbd", replacement.LastReplacement);
        Assert.Equal(4, engine.PendingTokenLength);
        Assert.False(early.HasPendingEarlyLayoutCorrection);
    }

    private static EarlyLayoutCorrectionService CreateEarlyService()
        => new(new EarlyLayoutSession(Model.Value));

    private static async Task TypeThroughEarlySwitchAsync(
        AutomaticLayoutCorrectionEngine engine,
        EarlyLayoutCorrectionService early,
        string token)
    {
        foreach (var character in token)
        {
            var prepared = early.ObserveCharacter(
                42,
                character,
                TypingLanguage.English,
                allowed: true);
            await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(
                character,
                prepared,
                prepared is not null));
        }
    }

    private sealed class RecordingLanguageService(bool success) : IKeyboardInputLanguageService
    {
        public KeyboardInputLanguage? LastRequestedLanguage { get; private set; }

        public Task<bool> SetForegroundInputLanguageAsync(
            KeyboardInputLanguage language,
            CancellationToken cancellationToken = default)
        {
            LastRequestedLanguage = language;
            return Task.FromResult(success);
        }
    }

    private sealed class NoPauseService : Platform.Abstractions.Safety.IEmergencyPauseService
    {
        public bool IsPaused => false;

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }

    private sealed class NoReplacementSessionNotifier : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => false;

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}
