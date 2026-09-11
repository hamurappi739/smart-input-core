using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class BackspaceRejectionLearningTests
{
    [Fact]
    public async Task OrdinaryBackspace_WithoutRecentCorrection_DoesNotRecordRejection()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);

        await backspaceService.ProcessInputAsync(TokenInputEvent.Backspace);

        Assert.Equal(0, learningStore.GetUndoCount("мущ", "veo", CorrectionKind.Layout));
    }

    [Fact]
    public async Task SingleBackspaceAfterCorrection_WithoutRetype_DoesNotRecordRejection()
    {
        var (undoService, backspaceService, learningStore) = CreateLinkedServices();

        undoService.RecordSuccessfulCorrection(CreateTransaction("мущ", "veo", CorrectionKind.Layout));

        await ProcessLiveInputAsync(backspaceService, undoService, TokenInputEvent.Backspace);

        Assert.Equal(0, learningStore.GetUndoCount("мущ", "veo", CorrectionKind.Layout));
    }

    [Fact]
    public async Task EraseReplacementAndRetypeOriginal_RecordsOneRejection()
    {
        var (undoService, backspaceService, learningStore) = CreateLinkedServices();

        undoService.RecordSuccessfulCorrection(CreateTransaction("мущ", "veo", CorrectionKind.Layout));
        await SimulateBackspaceRejectionAsync(backspaceService, undoService, "мущ", "veo");

        Assert.Equal(1, learningStore.GetUndoCount("мущ", "veo", CorrectionKind.Layout));
    }

    [Fact]
    public async Task TwoBackspaceRejections_BlockAutomaticCorrection()
    {
        var dictionary = CreateDictionaryWithWord("veo", TypingLanguage.English);
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);
        var undoService = CreateUndoService(learningStore, backspaceService);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            dictionary: dictionary,
            undoService: undoService,
            rejectionPolicy: new CorrectionRejectionPolicy(learningStore));

        undoService.RecordSuccessfulCorrection(CreateTransaction("мущ", "veo", CorrectionKind.Layout));
        await SimulateBackspaceRejectionAsync(backspaceService, undoService, "мущ", "veo");
        undoService.RecordSuccessfulCorrection(CreateTransaction("мущ", "veo", CorrectionKind.Layout));
        await SimulateBackspaceRejectionAsync(backspaceService, undoService, "мущ", "veo");

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(2, learningStore.GetUndoCount("мущ", "veo", CorrectionKind.Layout));
    }

    [Fact]
    public async Task AllowAgain_RemovesBackspaceRejectionBlock()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var policy = new CorrectionRejectionPolicy(learningStore);
        await learningStore.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "мущ",
            Replacement = "veo",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });

        Assert.True(policy.IsAutomaticallySuppressed("мущ", "veo", CorrectionKind.Layout));

        await policy.AllowAgainAsync("мущ", "veo", CorrectionKind.Layout);

        Assert.False(policy.IsAutomaticallySuppressed("мущ", "veo", CorrectionKind.Layout));
    }

    [Fact]
    public async Task DoubleShiftUndo_StillRecordsRejection()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(learningStore, replacement: replacement);

        undoService.RecordSuccessfulCorrection(CreateTransaction("ghbdtn", "привет", CorrectionKind.Layout));

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.Success, result.Outcome);
        Assert.Equal(1, learningStore.GetUndoCount("ghbdtn", "привет", CorrectionKind.Layout));
        Assert.Equal(1, replacement.CallCount);
    }

    [Fact]
    public async Task SafeMode_DoesNotCorrectOrLearn()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);
        var undoService = CreateUndoService(learningStore, backspaceService);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(AutomationPolicyState.SafeMode, allowsAutomation: false),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            undoService: undoService,
            rejectionPolicy: new CorrectionRejectionPolicy(learningStore));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, learningStore.GetUndoCount("ghbdtn", "привет", CorrectionKind.Layout));
    }

    [Fact]
    public async Task SecureInput_DoesNotCorrectOrLearn()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);
        var undoService = CreateUndoService(learningStore, backspaceService);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(AutomationPolicyState.SecureInput, allowsAutomation: false),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            undoService: undoService,
            rejectionPolicy: new CorrectionRejectionPolicy(learningStore));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, learningStore.GetUndoCount("ghbdtn", "привет", CorrectionKind.Layout));
    }

    [Fact]
    public async Task ProtectionDisabled_DoesNotCorrectOrLearn()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);
        var undoService = CreateUndoService(learningStore, backspaceService);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var settings = new AppSettings
        {
            IsEnabled = false,
            AutomaticLayoutEnabled = true,
        };
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            settings,
            replacement,
            undoService: undoService,
            rejectionPolicy: new CorrectionRejectionPolicy(learningStore));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, learningStore.GetUndoCount("ghbdtn", "привет", CorrectionKind.Layout));
    }

    [Fact]
    public async Task ExcludedApplicationPolicy_DoesNotCorrectOrLearn()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);
        var undoService = CreateUndoService(learningStore, backspaceService);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(AutomationPolicyState.BlockedApplication, allowsAutomation: false),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            undoService: undoService,
            rejectionPolicy: new CorrectionRejectionPolicy(learningStore));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, learningStore.GetUndoCount("ghbdtn", "привет", CorrectionKind.Layout));
    }

    [Fact]
    public async Task RejectionPersistenceCompletion_CannotInvalidateNewTrackingTransaction()
    {
        var learningStore = new BlockingLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);
        var oldTransaction = CreateTransaction("old", "new", CorrectionKind.Autocorrect);
        var newTransaction = CreateTransaction("fresh", "fixed", CorrectionKind.Autocorrect);

        backspaceService.BeginTracking(oldTransaction);
        foreach (var _ in oldTransaction.ReplacementToken)
        {
            await backspaceService.ProcessInputAsync(TokenInputEvent.Backspace);
        }

        var finalOldCharacter = Task.CompletedTask;
        foreach (var character in oldTransaction.OriginalToken)
        {
            var current = backspaceService.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
            if (character == oldTransaction.OriginalToken[^1])
            {
                finalOldCharacter = current;
            }
            else
            {
                await current;
            }
        }

        await learningStore.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        backspaceService.BeginTracking(newTransaction);
        learningStore.Release.TrySetResult();
        await finalOldCharacter;

        foreach (var _ in newTransaction.ReplacementToken)
        {
            await backspaceService.ProcessInputAsync(TokenInputEvent.Backspace);
        }

        foreach (var character in newTransaction.OriginalToken)
        {
            await backspaceService.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
        }

        var entries = await learningStore.GetEntriesAsync();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Candidate == oldTransaction.OriginalToken);
        Assert.Contains(entries, entry => entry.Candidate == newTransaction.OriginalToken);
    }

    private static (CorrectionUndoService Undo, CorrectionRejectionBackspaceService Backspace, InMemoryCorrectionRejectionLearningStore Learning)
        CreateLinkedServices()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var backspaceService = new CorrectionRejectionBackspaceService(learningStore);
        var undoService = CreateUndoService(learningStore, backspaceService);
        return (undoService, backspaceService, learningStore);
    }

    private static CorrectionUndoService CreateUndoService(
        InMemoryCorrectionRejectionLearningStore learningStore,
        CorrectionRejectionBackspaceService? backspaceService = null,
        ISafeTextReplacementService? replacement = null)
    {
        return new CorrectionUndoService(
            replacement ?? new TrackingReplacementService(),
            new LiveCorrectionTestHelpers.FakeAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy()),
            new LiveCorrectionTestHelpers.FakeSettingsService(LiveCorrectionTestHelpers.EnabledLayoutSettings()),
            learningStore,
            backspaceRejectionService: backspaceService);
    }

    private static CorrectionTransaction CreateTransaction(
        string original,
        string replacement,
        CorrectionKind kind)
    {
        return new CorrectionTransaction
        {
            OriginalToken = original,
            ReplacementToken = replacement,
            Kind = kind,
            TrailingText = " ",
        };
    }

    private static async Task SimulateBackspaceRejectionAsync(
        CorrectionRejectionBackspaceService backspaceService,
        CorrectionUndoService undoService,
        string original,
        string replacement)
    {
        for (var index = 0; index < replacement.Length; index++)
        {
            await ProcessLiveInputAsync(backspaceService, undoService, TokenInputEvent.Backspace);
        }

        foreach (var character in original)
        {
            await ProcessLiveInputAsync(backspaceService, undoService, TokenInputEvent.CharacterInput(character));
        }
    }

    private static async Task ProcessLiveInputAsync(
        CorrectionRejectionBackspaceService backspaceService,
        CorrectionUndoService undoService,
        TokenInputEvent input)
    {
        await backspaceService.ProcessInputAsync(input);
        if (input.Kind is TokenInputKind.Character or TokenInputKind.Backspace)
        {
            undoService.NotifyGenuineUserInput();
        }
    }

    private static CompositeAutocorrectDictionary CreateDictionaryWithWord(
        string word,
        TypingLanguage language)
    {
        var store = new MutableUserAutocorrectDictionaryStore();
        store.AddOrUpdate(new UserAutocorrectDictionaryEntry
        {
            Word = word,
            Language = language,
        });

        return new CompositeAutocorrectDictionary(store);
    }

    private sealed class MutableUserAutocorrectDictionaryStore : IUserAutocorrectDictionaryStore
    {
        private readonly List<UserAutocorrectDictionaryEntry> _entries = [];

        public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries => _entries;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
        {
            AddOrUpdate(entry);
            return Task.CompletedTask;
        }

        public void AddOrUpdate(UserAutocorrectDictionaryEntry entry)
        {
            var index = _entries.FindIndex(existing =>
                existing.Language == entry.Language
                && string.Equals(existing.Word, entry.Word, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _entries[index] = entry;
            }
            else
            {
                _entries.Add(entry);
            }
        }

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
        {
            _entries.RemoveAll(existing =>
                existing.Language == language
                && string.Equals(existing.Word, word, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static TokenInputEvent CreateSpaceBoundary()
    {
        return TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57));
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

    private sealed class BlockingLearningStore : ICorrectionRejectionLearningStore
    {
        private readonly InMemoryCorrectionRejectionLearningStore _inner = new();
        private int _blockNext = 1;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
            => _inner.EnsureLoadedAsync(cancellationToken);

        public Task ReloadAsync(CancellationToken cancellationToken = default)
            => _inner.EnsureLoadedAsync(cancellationToken);

        public int GetUndoCount(string candidate, string replacement, CorrectionKind kind)
            => _inner.GetUndoCount(candidate, replacement, kind);

        public async Task RecordRejectionAsync(
            CorrectionRejectionLearningEntry entry,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _blockNext, 0) == 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            await _inner.RecordRejectionAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<CorrectionRejectionLearningEntry>> GetEntriesAsync(
            CancellationToken cancellationToken = default)
            => _inner.GetEntriesAsync(cancellationToken);

        public Task RemoveAsync(
            string candidate,
            string replacement,
            CorrectionKind kind,
            CancellationToken cancellationToken = default)
            => _inner.RemoveAsync(candidate, replacement, kind, cancellationToken);
    }
}
