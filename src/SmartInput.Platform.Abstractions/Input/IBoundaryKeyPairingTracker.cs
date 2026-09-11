namespace SmartInput.Platform.Abstractions.Input;

public enum BoundaryPairingState
{
    Idle,
    AwaitingPhysicalKeyUp,
}

public enum BoundaryPairingCleanupReason
{
    None,
    PhysicalKeyUpMatched,
    Expired,
    FocusChange,
    EmergencyPause,
    HookShutdown,
    PolicyChange,
}

public interface IBoundaryKeyPairingTracker
{
    BoundaryPairingState State { get; }

    int KeyUpSuppressedCount { get; }

    bool RegisterSuppressedKeyDown(int virtualKeyCode, int scanCode);

    bool TrySuppressMatchingKeyUp(int virtualKeyCode, int scanCode);

    void ClearPending(BoundaryPairingCleanupReason reason);
}
