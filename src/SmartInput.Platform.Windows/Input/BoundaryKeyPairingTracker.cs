using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Platform.Windows.Input;

public sealed class BoundaryKeyPairingTracker : IBoundaryKeyPairingTracker
{
    /// <summary>
    /// Pending physical KeyUp may arrive while replacement/evaluation is still
    /// running. Keep the wait window above the replacement timeout so a held
    /// boundary key cannot leak a spurious KeyUp into the target application.
    /// </summary>
    public const int DefaultKeyUpWaitMilliseconds = 30_000;

    private readonly object _sync = new();
    private readonly int _keyUpWaitMilliseconds;
    private readonly ITextReplacementSessionNotifier? _replacementSessionNotifier;

    private BoundaryPairingState _state = BoundaryPairingState.Idle;
    private int _pendingVirtualKeyCode;
    private int _pendingScanCode;
    private long _registeredAtTickCount;
    private int _keyUpSuppressedCount;

    public BoundaryKeyPairingTracker(
        int keyUpWaitMilliseconds = DefaultKeyUpWaitMilliseconds,
        ITextReplacementSessionNotifier? replacementSessionNotifier = null)
    {
        _keyUpWaitMilliseconds = keyUpWaitMilliseconds;
        _replacementSessionNotifier = replacementSessionNotifier;
    }

    public BoundaryPairingState State
    {
        get
        {
            lock (_sync)
            {
                ExpireIfNeededLocked();
                return _state;
            }
        }
    }

    public int KeyUpSuppressedCount
    {
        get
        {
            lock (_sync)
            {
                return _keyUpSuppressedCount;
            }
        }
    }

    public bool RegisterSuppressedKeyDown(int virtualKeyCode, int scanCode)
    {
        lock (_sync)
        {
            ExpireIfNeededLocked();
            _pendingVirtualKeyCode = virtualKeyCode;
            _pendingScanCode = scanCode;
            _registeredAtTickCount = Environment.TickCount64;
            _state = BoundaryPairingState.AwaitingPhysicalKeyUp;
            return true;
        }
    }

    public bool TrySuppressMatchingKeyUp(int virtualKeyCode, int scanCode)
    {
        lock (_sync)
        {
            ExpireIfNeededLocked();
            if (_state != BoundaryPairingState.AwaitingPhysicalKeyUp)
            {
                return false;
            }

            if (virtualKeyCode != _pendingVirtualKeyCode)
            {
                return false;
            }

            if (_pendingScanCode != 0 && scanCode != 0 && scanCode != _pendingScanCode)
            {
                return false;
            }

            _state = BoundaryPairingState.Idle;
            _pendingVirtualKeyCode = 0;
            _pendingScanCode = 0;
            _keyUpSuppressedCount++;
            return true;
        }
    }

    public void ClearPending(BoundaryPairingCleanupReason reason)
    {
        _ = reason;

        lock (_sync)
        {
            _state = BoundaryPairingState.Idle;
            _pendingVirtualKeyCode = 0;
            _pendingScanCode = 0;
        }
    }

    private void ExpireIfNeededLocked()
    {
        if (_state != BoundaryPairingState.AwaitingPhysicalKeyUp)
        {
            return;
        }

        // A suppressed KeyDown's matching KeyUp must still be suppressible while
        // Smart Input is rewriting text; otherwise the target app receives a
        // KeyUp for a KeyDown it never saw.
        if (_replacementSessionNotifier?.IsReplacementActive == true)
        {
            return;
        }

        if (Environment.TickCount64 - _registeredAtTickCount > _keyUpWaitMilliseconds)
        {
            _state = BoundaryPairingState.Idle;
            _pendingVirtualKeyCode = 0;
            _pendingScanCode = 0;
        }
    }
}
