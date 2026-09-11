using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Services;

public interface ICorrectionUndoService
{
    CorrectionUndoStatus Status { get; }

    bool IsUndoAvailable { get; }

    CorrectionKind? PendingCorrectionKind { get; }

    void RecordSuccessfulCorrection(CorrectionTransaction transaction);

    void Invalidate(CorrectionUndoInvalidationReason reason);

    void NotifyGenuineUserInput();

    void NotifyApplicationContextChanged(string? processName, nint windowHandle);

    void NotifyPolicyContextChanged(AutomationPolicyResult policy);

    Task<CorrectionUndoResult> TryUndoAsync(CancellationToken cancellationToken = default);
}

public sealed class CorrectionUndoService : ICorrectionUndoService
{
    private readonly ISafeTextReplacementService _safeTextReplacementService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly ISettingsService _settingsService;
    private readonly ICorrectionRejectionLearningStore _learningStore;
    private readonly ICorrectionFeedbackNotifier _feedbackNotifier;
    private readonly ICorrectionRejectionBackspaceService? _backspaceRejectionService;
    private readonly CorrectionUndoOptions _options;
    private readonly object _sync = new();

    private CorrectionTransaction? _pendingTransaction;
    private int _undoAttempts;
    private int _undoSucceeded;
    private int _undoBlocked;
    private int _undoFailed;
    private int _undoInvalidated;
    private int _learningRejectionsRecorded;
    private long _transactionVersion;
    private int _undoInProgress;

    public CorrectionUndoService(
        ISafeTextReplacementService safeTextReplacementService,
        IAutomationSafetyService automationSafetyService,
        ISettingsService settingsService,
        ICorrectionRejectionLearningStore learningStore,
        CorrectionUndoOptions? options = null,
        ICorrectionFeedbackNotifier? feedbackNotifier = null,
        ICorrectionRejectionBackspaceService? backspaceRejectionService = null)
    {
        _safeTextReplacementService = safeTextReplacementService;
        _automationSafetyService = automationSafetyService;
        _settingsService = settingsService;
        _learningStore = learningStore;
        _feedbackNotifier = feedbackNotifier ?? NullCorrectionFeedbackNotifier.Instance;
        _backspaceRejectionService = backspaceRejectionService;
        _options = options ?? new CorrectionUndoOptions();
    }

    public CorrectionUndoStatus Status
    {
        get
        {
            lock (_sync)
            {
                RefreshExpirationLocked();
                return BuildStatusLocked();
            }
        }
    }

    public bool IsUndoAvailable
    {
        get
        {
            lock (_sync)
            {
                RefreshExpirationLocked();
                return _pendingTransaction is not null;
            }
        }
    }

    public CorrectionKind? PendingCorrectionKind
    {
        get
        {
            lock (_sync)
            {
                RefreshExpirationLocked();
                return _pendingTransaction?.Kind;
            }
        }
    }

    public void RecordSuccessfulCorrection(CorrectionTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        lock (_sync)
        {
            if (_pendingTransaction is not null)
            {
                InvalidateLocked(CorrectionUndoInvalidationReason.SupersededByNewCorrection);
            }

            _pendingTransaction = transaction;
            _transactionVersion++;
            _backspaceRejectionService?.BeginTracking(transaction);
        }
    }

    public void Invalidate(CorrectionUndoInvalidationReason reason)
    {
        lock (_sync)
        {
            InvalidateLocked(reason);
        }
    }

    public void NotifyGenuineUserInput()
    {
        lock (_sync)
        {
            if (_pendingTransaction is null)
            {
                return;
            }

            InvalidateLocked(CorrectionUndoInvalidationReason.GenuineUserInput);
        }
    }

    public void NotifyApplicationContextChanged(string? processName, nint windowHandle)
    {
        lock (_sync)
        {
            if (_pendingTransaction is null)
            {
                return;
            }

            var recordedProcess = _pendingTransaction.ApplicationProcessName;
            var recordedHandle = _pendingTransaction.ApplicationWindowHandle;

            if (recordedHandle != 0 && windowHandle != recordedHandle)
            {
                InvalidateLocked(CorrectionUndoInvalidationReason.ApplicationContextChanged);
                return;
            }

            if (!string.IsNullOrEmpty(recordedProcess)
                && !string.Equals(recordedProcess, processName, StringComparison.OrdinalIgnoreCase))
            {
                InvalidateLocked(CorrectionUndoInvalidationReason.ApplicationContextChanged);
            }
        }
    }

    public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        lock (_sync)
        {
            if (_pendingTransaction is null)
            {
                return;
            }

            if (policy.IsEmergencyPaused)
            {
                InvalidateLocked(CorrectionUndoInvalidationReason.EmergencyPause);
                return;
            }

            if (!IsPolicyAllowedForUndo(policy))
            {
                InvalidateLocked(CorrectionUndoInvalidationReason.PolicyChanged);
            }
        }
    }

    public async Task<CorrectionUndoResult> TryUndoAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _undoInProgress, 1, 0) != 0)
        {
            return CorrectionUndoResult.NotAvailable("Undo is already in progress.");
        }

        try
        {
            return await TryUndoCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _undoInProgress, 0);
        }
    }

    private async Task<CorrectionUndoResult> TryUndoCoreAsync(CancellationToken cancellationToken)
    {
        CorrectionTransaction? transaction;
        CorrectionKind kind;
        long transactionVersion;

        lock (_sync)
        {
            RefreshExpirationLocked();
            transaction = _pendingTransaction;
            if (transaction is null)
            {
                return CorrectionUndoResult.NotAvailable();
            }

            kind = transaction.Kind;
            transactionVersion = _transactionVersion;
            _undoAttempts++;
        }

        if (!_settingsService.Current.IsEnabled)
        {
            TryClearAfterUndoAttempt(
                transaction,
                transactionVersion,
                CorrectionUndoInvalidationReason.UndoBlocked);
            Interlocked.Increment(ref _undoBlocked);
            return CorrectionUndoResult.Blocked("Undo is unavailable while Protection is disabled.");
        }

        var policy = await _automationSafetyService
            .EvaluateCurrentContextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!IsCurrentTransaction(transaction, transactionVersion))
        {
            Interlocked.Increment(ref _undoFailed);
            return CorrectionUndoResult.Aborted(kind);
        }

        if (policy.IsEmergencyPaused || !IsPolicyAllowedForUndo(policy))
        {
            TryClearAfterUndoAttempt(
                transaction,
                transactionVersion,
                CorrectionUndoInvalidationReason.UndoBlocked);
            Interlocked.Increment(ref _undoBlocked);
            return CorrectionUndoResult.Blocked(
                policy.Reason ?? "Undo is blocked by the current safety policy.");
        }

        // The policy query is asynchronous. Revalidate immediately before
        // touching the target so a focus/input/replacement event cannot make
        // this operation act on an older transaction.
        if (!IsCurrentTransaction(transaction, transactionVersion))
        {
            Interlocked.Increment(ref _undoFailed);
            return CorrectionUndoResult.Aborted(kind);
        }

        TextReplacementResult replacement;
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromMilliseconds(_options.ReplacementTimeoutMilliseconds));

            replacement = await _safeTextReplacementService
                .ReplaceRecentTextAsync(
                    transaction.ReplacementToken + transaction.TrailingText,
                    transaction.OriginalToken + transaction.TrailingText,
                    timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryClearAfterUndoAttempt(
                transaction,
                transactionVersion,
                CorrectionUndoInvalidationReason.UndoFailed);
            Interlocked.Increment(ref _undoFailed);
            return CorrectionUndoResult.Failed("Undo replacement timed out.", kind);
        }

        switch (replacement.Status)
        {
            case TextReplacementStatus.Success:
                if (!IsCurrentTransaction(transaction, transactionVersion))
                {
                    Interlocked.Increment(ref _undoFailed);
                    return CorrectionUndoResult.Aborted(kind);
                }

                await RecordLearningRejectionAsync(transaction, cancellationToken).ConfigureAwait(false);
                if (!TryClearAfterUndoAttempt(
                        transaction,
                        transactionVersion,
                        CorrectionUndoInvalidationReason.UndoCompleted))
                {
                    // A newer transaction or genuine input won the race while
                    // learning persistence was awaiting I/O. Do not report a
                    // success that would make the caller clear current text.
                    Interlocked.Increment(ref _undoFailed);
                    return CorrectionUndoResult.Aborted(kind);
                }

                _feedbackNotifier.NotifyCorrectionUndone();
                Interlocked.Increment(ref _undoSucceeded);
                return CorrectionUndoResult.Succeeded(kind);

            case TextReplacementStatus.Blocked:
                TryClearAfterUndoAttempt(
                    transaction,
                    transactionVersion,
                    CorrectionUndoInvalidationReason.UndoBlocked);
                Interlocked.Increment(ref _undoBlocked);
                return CorrectionUndoResult.Blocked(
                    replacement.FailureReason ?? "Undo was blocked by safety policy.");

            case TextReplacementStatus.AbortedByUserInput:
                TryClearAfterUndoAttempt(
                    transaction,
                    transactionVersion,
                    CorrectionUndoInvalidationReason.UndoFailed);
                Interlocked.Increment(ref _undoFailed);
                return CorrectionUndoResult.Aborted(kind);

            default:
                TryClearAfterUndoAttempt(
                    transaction,
                    transactionVersion,
                    CorrectionUndoInvalidationReason.UndoFailed);
                Interlocked.Increment(ref _undoFailed);
                return CorrectionUndoResult.Failed(
                    replacement.FailureReason ?? "Undo replacement failed.",
                    kind);
        }
    }

    private async Task RecordLearningRejectionAsync(
        CorrectionTransaction transaction,
        CancellationToken cancellationToken)
    {
        await _learningStore
            .RecordRejectionAsync(
                new CorrectionRejectionLearningEntry
                {
                    Candidate = transaction.OriginalToken,
                    Replacement = transaction.ReplacementToken,
                    Kind = transaction.Kind,
                    UndoCount = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        Interlocked.Increment(ref _learningRejectionsRecorded);
    }

    private bool TryClearAfterUndoAttempt(
        CorrectionTransaction transaction,
        long transactionVersion,
        CorrectionUndoInvalidationReason reason)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(_pendingTransaction, transaction)
                || _transactionVersion != transactionVersion)
            {
                return false;
            }

            InvalidateLocked(reason);
            return true;
        }
    }

    private bool IsCurrentTransaction(CorrectionTransaction transaction, long transactionVersion)
    {
        lock (_sync)
        {
            return ReferenceEquals(_pendingTransaction, transaction)
                && _transactionVersion == transactionVersion;
        }
    }

    private void InvalidateLocked(CorrectionUndoInvalidationReason reason)
    {
        if (_pendingTransaction is null)
        {
            return;
        }

        _pendingTransaction = null;
        _transactionVersion++;
        _undoInvalidated++;
        _ = reason;
    }

    private void RefreshExpirationLocked()
    {
        if (_pendingTransaction is null)
        {
            return;
        }

        var expiresAt = _pendingTransaction.RecordedAt
            .AddSeconds(_options.TransactionTimeoutSeconds);

        if (DateTimeOffset.UtcNow >= expiresAt)
        {
            InvalidateLocked(CorrectionUndoInvalidationReason.Expired);
        }
    }

    private CorrectionUndoStatus BuildStatusLocked()
    {
        return new CorrectionUndoStatus
        {
            IsAvailable = _pendingTransaction is not null,
            PendingKind = _pendingTransaction?.Kind,
            UndoAttempts = _undoAttempts,
            UndoSucceeded = _undoSucceeded,
            UndoBlocked = _undoBlocked,
            UndoFailed = _undoFailed,
            UndoInvalidated = _undoInvalidated,
            LearningRejectionsRecorded = _learningRejectionsRecorded,
        };
    }

    private static bool IsPolicyAllowedForUndo(AutomationPolicyResult policy)
    {
        return policy.State == AutomationPolicyState.Allowed
            && policy.AllowsAutomation
            && !policy.IsEmergencyPaused;
    }
}
