using SmartInput.Core.Configuration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Core.Services;

public interface ICorrectionRejectionBackspaceService
{
    void BeginTracking(CorrectionTransaction transaction);

    Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default);

    void Invalidate(CorrectionRejectionBackspaceInvalidationReason reason);

    void NotifyApplicationContextChanged(string? processName, nint windowHandle);

    void NotifyPolicyContextChanged(AutomationPolicyResult policy);
}

public enum CorrectionRejectionBackspaceInvalidationReason
{
    SupersededByNewCorrection,
    GenuineUserInput,
    ApplicationContextChanged,
    EmergencyPause,
    PolicyChanged,
    Expired,
    PatternAborted,
    RejectionRecorded,
}

public sealed class CorrectionRejectionBackspaceService : ICorrectionRejectionBackspaceService
{
    private enum TrackingPhase
    {
        None,
        ErasingReplacement,
        AwaitingOriginalRetype,
    }

    private readonly ICorrectionRejectionLearningStore _learningStore;
    private readonly CorrectionUndoOptions _options;
    private readonly object _sync = new();

    private CorrectionTransaction? _pendingTransaction;
    private long _transactionVersion;
    private TrackingPhase _phase;
    private int _backspacesRemaining;
    private readonly System.Text.StringBuilder _retypeBuffer = new();

    public CorrectionRejectionBackspaceService(
        ICorrectionRejectionLearningStore learningStore,
        CorrectionUndoOptions? options = null)
    {
        _learningStore = learningStore;
        _options = options ?? new CorrectionUndoOptions();
    }

    public void BeginTracking(CorrectionTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!CanTrack(transaction))
        {
            return;
        }

        lock (_sync)
        {
            if (_pendingTransaction is not null)
            {
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.SupersededByNewCorrection);
            }

            _pendingTransaction = transaction;
            _transactionVersion++;
            _phase = TrackingPhase.ErasingReplacement;
            _backspacesRemaining = transaction.ReplacementToken.Length;
            _retypeBuffer.Clear();
        }
    }

    public async Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_sync)
        {
            RefreshExpirationLocked();
            if (_pendingTransaction is null || _phase == TrackingPhase.None)
            {
                return;
            }
        }

        switch (input.Kind)
        {
            case TokenInputKind.Backspace:
                HandleBackspace();
                break;

            case TokenInputKind.Character:
                await HandleCharacterAsync(input.Character, cancellationToken).ConfigureAwait(false);
                break;

            case TokenInputKind.WordBoundary:
            case TokenInputKind.Reset:
            case TokenInputKind.Uncertain:
                Invalidate(CorrectionRejectionBackspaceInvalidationReason.PatternAborted);
                break;
        }
    }

    public void Invalidate(CorrectionRejectionBackspaceInvalidationReason reason)
    {
        lock (_sync)
        {
            InvalidateLocked(reason);
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
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.ApplicationContextChanged);
                return;
            }

            if (!string.IsNullOrEmpty(recordedProcess)
                && !string.Equals(recordedProcess, processName, StringComparison.OrdinalIgnoreCase))
            {
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.ApplicationContextChanged);
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
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.EmergencyPause);
                return;
            }

            if (!IsPolicyAllowed(policy))
            {
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.PolicyChanged);
            }
        }
    }

    private void HandleBackspace()
    {
        TrackingPhase phase;
        lock (_sync)
        {
            phase = _phase;
            if (_pendingTransaction is null)
            {
                return;
            }

            if (phase == TrackingPhase.ErasingReplacement)
            {
                _backspacesRemaining--;
                if (_backspacesRemaining <= 0)
                {
                    _phase = TrackingPhase.AwaitingOriginalRetype;
                }

                return;
            }

            if (phase == TrackingPhase.AwaitingOriginalRetype)
            {
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.PatternAborted);
                return;
            }
        }
    }

    private async Task HandleCharacterAsync(char character, CancellationToken cancellationToken)
    {
        CorrectionTransaction? transaction;
        long transactionVersion;
        TrackingPhase phase;

        lock (_sync)
        {
            phase = _phase;
            transaction = _pendingTransaction;
            transactionVersion = _transactionVersion;
            if (transaction is null)
            {
                return;
            }

            if (phase == TrackingPhase.ErasingReplacement)
            {
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.PatternAborted);
                return;
            }

            if (phase != TrackingPhase.AwaitingOriginalRetype)
            {
                return;
            }

            _retypeBuffer.Append(character);
            var typed = _retypeBuffer.ToString();
            if (!transaction.OriginalToken.StartsWith(typed, StringComparison.Ordinal))
            {
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.PatternAborted);
                return;
            }

            if (typed.Length < transaction.OriginalToken.Length)
            {
                return;
            }
        }

        await RecordRejectionAsync(transaction!, cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            if (ReferenceEquals(_pendingTransaction, transaction)
                && _transactionVersion == transactionVersion)
            {
                InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.RejectionRecorded);
            }
        }
    }

    private async Task RecordRejectionAsync(
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
    }

    private static bool CanTrack(CorrectionTransaction transaction)
    {
        if (transaction.OriginalToken.Length <= 1
            || transaction.ReplacementToken.Length <= 1)
        {
            return false;
        }

        return !ProtectedTokenAnalyzer.IsProtected(transaction.OriginalToken)
            && !ProtectedTokenAnalyzer.IsProtected(transaction.ReplacementToken);
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
            InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason.Expired);
        }
    }

    private void InvalidateLocked(CorrectionRejectionBackspaceInvalidationReason reason)
    {
        if (_pendingTransaction is null)
        {
            return;
        }

        _pendingTransaction = null;
        _transactionVersion++;
        _phase = TrackingPhase.None;
        _backspacesRemaining = 0;
        _retypeBuffer.Clear();
        _ = reason;
    }

    private static bool IsPolicyAllowed(AutomationPolicyResult policy)
    {
        return policy.State == AutomationPolicyState.Allowed
            && policy.AllowsAutomation
            && !policy.IsEmergencyPaused;
    }
}
