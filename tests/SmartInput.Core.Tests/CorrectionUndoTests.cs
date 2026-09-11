using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class CorrectionUndoTests
{
    [Fact]
    public async Task TryUndoAsync_AfterSuccessfulCorrection_RestoresOriginalToken()
    {
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(replacement, LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.Success, result.Outcome);
        Assert.Equal(CorrectionKind.Autocorrect, result.Kind);
        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("hello", replacement.LastOriginal);
        Assert.Equal("helo", replacement.LastReplacement);
    }

    [Fact]
    public async Task TryUndoAsync_AfterSpaceBoundary_PreservesTheSpace()
    {
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(replacement, LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(new CorrectionTransaction
        {
            OriginalToken = "ghbdtn",
            ReplacementToken = "привет",
            TrailingText = " ",
            Kind = CorrectionKind.Layout,
        });

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.Success, result.Outcome);
        Assert.Equal("привет ", replacement.LastOriginal);
        Assert.Equal("ghbdtn ", replacement.LastReplacement);
    }

    [Fact]
    public void IsUndoAvailable_OnlyAfterSuccessfulCorrection()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy());

        Assert.False(undoService.IsUndoAvailable);

        undoService.RecordSuccessfulCorrection(CreateTransaction("ghbdtn", "привет", CorrectionKind.Layout));

        Assert.True(undoService.IsUndoAvailable);
        Assert.Equal(CorrectionKind.Layout, undoService.PendingCorrectionKind);
    }

    [Fact]
    public async Task TryUndoAsync_ClearsTransactionAfterUse()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        await undoService.TryUndoAsync();

        Assert.False(undoService.IsUndoAvailable);
        Assert.Null(undoService.PendingCorrectionKind);
    }

    [Fact]
    public async Task TryUndoAsync_WhenPolicyBlocked_FailsSafelyAndClearsTransaction()
    {
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(
            replacement,
            LiveCorrectionTestHelpers.Policy(AutomationPolicyState.SecureInput, allowsAutomation: false));

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.Blocked, result.Outcome);
        Assert.Equal(0, replacement.CallCount);
        Assert.False(undoService.IsUndoAvailable);
        Assert.Equal(1, undoService.Status.UndoBlocked);
    }

    [Fact]
    public void NotifyGenuineUserInput_InvalidatesUndo()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));
        undoService.NotifyGenuineUserInput();

        Assert.False(undoService.IsUndoAvailable);
        Assert.True(undoService.Status.UndoInvalidated >= 1);
    }

    [Fact]
    public void NotifyApplicationContextChanged_InvalidatesUndoWhenHandleChanges()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(new CorrectionTransaction
        {
            OriginalToken = "helo",
            ReplacementToken = "hello",
            Kind = CorrectionKind.Autocorrect,
            ApplicationProcessName = "notepad",
            ApplicationWindowHandle = 100,
        });

        undoService.NotifyApplicationContextChanged("notepad", 200);

        Assert.False(undoService.IsUndoAvailable);
    }

    [Fact]
    public void NotifyPolicyContextChanged_EmergencyPause_InvalidatesUndo()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));
        undoService.NotifyPolicyContextChanged(AutomationPolicyResult.EmergencyPaused());

        Assert.False(undoService.IsUndoAvailable);
    }

    [Fact]
    public void RecordSuccessfulCorrection_SecondCorrection_ReplacesPreviousTransaction()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));
        undoService.RecordSuccessfulCorrection(CreateTransaction("ghbdtn", "привет", CorrectionKind.Layout));

        Assert.True(undoService.IsUndoAvailable);
        Assert.Equal(CorrectionKind.Layout, undoService.PendingCorrectionKind);
    }

    [Fact]
    public async Task TryUndoAsync_AfterTransactionExpires_ReturnsExpiredAndClears()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy(),
            options: new CorrectionUndoOptions { TransactionTimeoutSeconds = 0 });

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        await Task.Delay(10);

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.NotAvailable, result.Outcome);
        Assert.False(undoService.IsUndoAvailable);
    }

    [Fact]
    public async Task EngineSuccessfulLayoutCorrection_RecordsLayoutKindForUndo()
    {
        var undoService = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(CorrectionKind.Layout, undoService.LastRecordedTransaction?.Kind);
        Assert.Equal("ghbdtn", undoService.LastRecordedTransaction?.OriginalToken);
        Assert.Equal("привет", undoService.LastRecordedTransaction?.ReplacementToken);
        Assert.Equal(" ", undoService.LastRecordedTransaction?.TrailingText);
    }

    [Fact]
    public async Task EngineSuccessfulAutocorrect_RecordsAutocorrectKindForUndo()
    {
        var undoService = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(CorrectionKind.Autocorrect, undoService.LastRecordedTransaction?.Kind);
    }

    [Fact]
    public async Task TryUndoAsync_Success_RecordsLearningRejection()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(
            replacement,
            LiveCorrectionTestHelpers.AllowedPolicy(),
            learningStore: learningStore);

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        await undoService.TryUndoAsync();

        var entries = await learningStore.GetEntriesAsync();
        Assert.Single(entries);
        Assert.Equal("helo", entries[0].Candidate);
        Assert.Equal("hello", entries[0].Replacement);
        Assert.Equal(CorrectionKind.Autocorrect, entries[0].Kind);
        Assert.Equal(1, entries[0].UndoCount);
        Assert.Equal(1, undoService.Status.LearningRejectionsRecorded);
    }

    [Fact]
    public async Task TryUndoAsync_Failure_DoesNotRecordLearningRejection()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var replacement = new TrackingReplacementService(
            (_, _) => TextReplacementResult.Blocked("blocked"));
        var undoService = CreateUndoService(
            replacement,
            LiveCorrectionTestHelpers.AllowedPolicy(),
            learningStore: learningStore);

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        await undoService.TryUndoAsync();

        var entries = await learningStore.GetEntriesAsync();
        Assert.Empty(entries);
        Assert.Equal(0, undoService.Status.LearningRejectionsRecorded);
    }

    [Fact]
    public async Task LearningStore_PersistenceRoundTrip_PreservesAggregatedEntries()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"smartinput-learning-{Guid.NewGuid():N}.json");
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonCorrectionRejectionLearningPersistence>.Instance;
        var persistence = new JsonCorrectionRejectionLearningPersistence(filePath, logger);

        var store = new CorrectionRejectionLearningStore(persistence);
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "helo",
            Replacement = "hello",
            Kind = CorrectionKind.Autocorrect,
            UndoCount = 1,
        });

        var reloadedStore = new CorrectionRejectionLearningStore(persistence);
        var entries = await reloadedStore.GetEntriesAsync();

        Assert.Single(entries);
        Assert.Equal("helo", entries[0].Candidate);
        Assert.Equal("hello", entries[0].Replacement);
        Assert.Equal(1, entries[0].UndoCount);

        File.Delete(filePath);
    }

    [Fact]
    public async Task LearningStore_ReloadAsync_ReflectsAllowAgainFromAnotherProcess()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"smartinput-learning-reload-{Guid.NewGuid():N}.json");
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonCorrectionRejectionLearningPersistence>.Instance;
        var persistence = new JsonCorrectionRejectionLearningPersistence(filePath, logger);
        var residentStore = new CorrectionRejectionLearningStore(persistence);

        await residentStore.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "ghbdtn",
            Replacement = "привет",
            Kind = CorrectionKind.Layout,
            UndoCount = 2,
        });
        Assert.Equal(2, residentStore.GetUndoCount("ghbdtn", "привет", CorrectionKind.Layout));

        var settingsProcessStore = new CorrectionRejectionLearningStore(persistence);
        await settingsProcessStore.RemoveAsync("ghbdtn", "привет", CorrectionKind.Layout);

        await residentStore.ReloadAsync();

        Assert.Equal(0, residentStore.GetUndoCount("ghbdtn", "привет", CorrectionKind.Layout));
        File.Delete(filePath);
    }

    [Fact]
    public async Task LearningPersistence_MalformedFile_FailsOpenWithoutThrowing()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"smartinput-learning-malformed-{Guid.NewGuid():N}.json");

        try
        {
            await File.WriteAllTextAsync(filePath, "{ malformed local state");
            var persistence = new JsonCorrectionRejectionLearningPersistence(
                filePath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonCorrectionRejectionLearningPersistence>.Instance);

            var entries = await persistence.LoadAsync();

            Assert.Empty(entries);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task LearningPersistence_AtomicSave_LeavesValidDestinationAndNoTemporaryFiles()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"smartinput-learning-atomic-{Guid.NewGuid():N}.json");

        try
        {
            var persistence = new JsonCorrectionRejectionLearningPersistence(
                filePath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonCorrectionRejectionLearningPersistence>.Instance);

            await persistence.SaveAsync(
            [
                new CorrectionRejectionLearningEntry
                {
                    Candidate = "candidate",
                    Replacement = "replacement",
                    Kind = CorrectionKind.Autocorrect,
                    UndoCount = 1,
                },
            ]);

            var entries = await persistence.LoadAsync();

            Assert.Single(entries);
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileName(filePath)}.*.tmp"));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task LearningStore_ConcurrentRecords_PersistAllEntries()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"smartinput-learning-concurrent-{Guid.NewGuid():N}.json");

        try
        {
            var persistence = new JsonCorrectionRejectionLearningPersistence(
                filePath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonCorrectionRejectionLearningPersistence>.Instance);
            var store = new CorrectionRejectionLearningStore(persistence);

            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => store.RecordRejectionAsync(
                new CorrectionRejectionLearningEntry
                {
                    Candidate = $"candidate-{index}",
                    Replacement = $"replacement-{index}",
                    Kind = CorrectionKind.Autocorrect,
                    UndoCount = 1,
                })));

            var reloaded = new CorrectionRejectionLearningStore(persistence);
            var entries = await reloaded.GetEntriesAsync();

            Assert.Equal(8, entries.Count);
            Assert.All(entries, entry => Assert.Equal(1, entry.UndoCount));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task LearningStore_DuplicateRecords_AggregateDeterministically()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();

        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "helo",
            Replacement = "hello",
            Kind = CorrectionKind.Autocorrect,
            UndoCount = 1,
        });

        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "helo",
            Replacement = "hello",
            Kind = CorrectionKind.Autocorrect,
            UndoCount = 1,
        });

        var entries = await store.GetEntriesAsync();
        Assert.Single(entries);
        Assert.Equal(2, entries[0].UndoCount);
    }

    [Fact]
    public void UndoStatus_ToString_DoesNotContainTokenText()
    {
        var undoService = CreateUndoService(
            new TrackingReplacementService(),
            LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        var statusText = undoService.Status.ToString();
        Assert.DoesNotContain("helo", statusText, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", statusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryUndoAsync_AbortedByUserInput_ClearsTransaction()
    {
        var replacement = new TrackingReplacementService(
            (_, _) => TextReplacementResult.AbortedByUserInput(2, 1));
        var undoService = CreateUndoService(
            replacement,
            LiveCorrectionTestHelpers.AllowedPolicy());

        undoService.RecordSuccessfulCorrection(CreateTransaction("helo", "hello", CorrectionKind.Autocorrect));

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.AbortedByUserInput, result.Outcome);
        Assert.False(undoService.IsUndoAvailable);
    }

    [Fact]
    public async Task TryUndoAsync_WhenInputInvalidatesDuringPolicyWait_DoesNotTouchTarget()
    {
        var safety = new BlockingAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy());
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(
            replacement,
            LiveCorrectionTestHelpers.AllowedPolicy(),
            safetyService: safety);
        undoService.RecordSuccessfulCorrection(CreateTransaction("old", "new", CorrectionKind.Autocorrect));

        var undoTask = undoService.TryUndoAsync();
        await safety.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        undoService.NotifyGenuineUserInput();
        safety.Release.TrySetResult();

        var result = await undoTask;

        Assert.Equal(CorrectionUndoOutcome.AbortedByUserInput, result.Outcome);
        Assert.Equal(0, replacement.CallCount);
        Assert.False(undoService.IsUndoAvailable);
    }

    [Fact]
    public async Task TryUndoAsync_WhenNewCorrectionReplacesPending_DoesNotClearNewTransaction()
    {
        var safety = new BlockingAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy());
        var replacement = new TrackingReplacementService();
        var undoService = CreateUndoService(
            replacement,
            LiveCorrectionTestHelpers.AllowedPolicy(),
            safetyService: safety);
        undoService.RecordSuccessfulCorrection(CreateTransaction("old", "new", CorrectionKind.Autocorrect));

        var undoTask = undoService.TryUndoAsync();
        await safety.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        undoService.RecordSuccessfulCorrection(CreateTransaction("layout-old", "layout-new", CorrectionKind.Layout));
        safety.Release.TrySetResult();

        var result = await undoTask;

        Assert.Equal(CorrectionUndoOutcome.AbortedByUserInput, result.Outcome);
        Assert.Equal(0, replacement.CallCount);
        Assert.True(undoService.IsUndoAvailable);
        Assert.Equal(CorrectionKind.Layout, undoService.PendingCorrectionKind);
    }

    [Fact]
    public async Task TryUndoAsync_ConcurrentCalls_OnlyOneReplacementRuns()
    {
        var replacement = new BlockingReplacementService();
        var undoService = CreateUndoService(
            replacement,
            LiveCorrectionTestHelpers.AllowedPolicy());
        undoService.RecordSuccessfulCorrection(CreateTransaction("old", "new", CorrectionKind.Autocorrect));

        var firstUndo = undoService.TryUndoAsync();
        await replacement.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondResult = await undoService.TryUndoAsync();

        replacement.Release.TrySetResult();
        var firstResult = await firstUndo;

        Assert.Equal(CorrectionUndoOutcome.NotAvailable, secondResult.Outcome);
        Assert.Equal(CorrectionUndoOutcome.Success, firstResult.Outcome);
        Assert.Equal(1, replacement.CallCount);
    }

    private static CorrectionUndoService CreateUndoService(
        ISafeTextReplacementService replacement,
        AutomationPolicyResult policy,
        ICorrectionRejectionLearningStore? learningStore = null,
        CorrectionUndoOptions? options = null,
        IAutomationSafetyService? safetyService = null)
    {
        return new CorrectionUndoService(
            replacement,
            safetyService ?? new LiveCorrectionTestHelpers.FakeAutomationSafetyService(policy),
            new LiveCorrectionTestHelpers.FakeSettingsService(new AppSettings { IsEnabled = true }),
            learningStore ?? new InMemoryCorrectionRejectionLearningStore(),
            options);
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
        };
    }

    private sealed class TrackingReplacementService : ISafeTextReplacementService
    {
        private readonly Func<string, string, TextReplacementResult> _handler;

        public TrackingReplacementService()
            : this((original, replacement) => TextReplacementResult.Success(original.Length, replacement.Length))
        {
        }

        public TrackingReplacementService(Func<string, string, TextReplacementResult> handler)
        {
            _handler = handler;
        }

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
            return Task.FromResult(_handler(originalText, replacementText));
        }

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
        {
            return ReplaceRecentTextAsync("abc", "xyz", cancellationToken);
        }

        public Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
        {
            return ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
        }
    }

    private sealed class BlockingAutomationSafetyService(AutomationPolicyResult policy)
        : IAutomationSafetyService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AutomationPolicyResult EvaluateCurrentContext() => policy;

        public async Task<AutomationPolicyResult> EvaluateCurrentContextAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return policy;
        }

        public bool IsOperationAllowed(AutomationOperationKind operationKind)
            => policy.IsAllowed(operationKind);

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
            => Task.FromResult(policy.IsAllowed(operationKind));

        public string? GetBlockedReason(AutomationOperationKind operationKind)
            => policy.IsAllowed(operationKind) ? null : policy.Reason;
    }

    private sealed class BlockingReplacementService : ISafeTextReplacementService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return TextReplacementResult.Success(originalText.Length, replacementText.Length);
        }

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync("abc", "xyz", cancellationToken);

        public Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
    }
}
