namespace SmartInput.Platform.Abstractions.Input;

public sealed class KeyboardObservationEventArgs : EventArgs
{
    public int VirtualKeyCode { get; init; }

    public int ScanCode { get; init; }

    /// <summary>
    /// Low-level hook flags needed to faithfully replay a deferred physical
    /// key event. Only the platform event metadata is retained; no text is
    /// derived or stored here.
    /// </summary>
    public uint HookFlags { get; init; }

    public KeyEventType EventType { get; init; }

    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// Foreground window captured at hook time. This is an in-memory context
    /// token used to keep the hook-thread preflight aligned with the queued
    /// coordinator. It is never logged, persisted, or sent to Rust.
    /// </summary>
    public nint ForegroundWindowHandle { get; init; }

    public DeferredBoundaryKey? SuppressedBoundary { get; init; }

    public PreparedLayoutCorrection? PreparedLayoutCorrection { get; init; }

    /// <summary>
    /// True when PreparedLayoutCorrection belongs to the current character,
    /// not to a suppressed word boundary.
    /// </summary>
    public bool IsEarlyLayoutCorrection { get; init; }

    /// <summary>
    /// Full token captured by the hook-thread preflight before a boundary was
    /// delivered. It is carried only in memory so the asynchronous coordinator
    /// cannot operate on a shorter token after a focus change or event queue
    /// delay. It must never be logged or persisted.
    /// </summary>
    public string? CompletedToken { get; init; }

    /// <summary>
    /// Character or boundary resolved synchronously on the hook thread for a
    /// deferred user event. A virtual-key replay after an automatic layout
    /// switch can produce a different character, so the original resolution
    /// is carried in memory through the replay only.
    /// </summary>
    public char? ResolvedCharacter { get; init; }

    /// <summary>
    /// Boundary resolved synchronously on the hook thread for a deferred user
    /// event. This is in-memory replay metadata and must never be logged or
    /// persisted.
    /// </summary>
    public DeferredBoundaryKey? ResolvedBoundary { get; init; }

    /// <summary>
    /// Complete hook-thread resolution for this observation. Keeping the
    /// resolution object prevents the asynchronous dispatcher from calling
    /// ToUnicodeEx again after the active layout has changed. It is strictly
    /// in-memory metadata and must never be logged or persisted.
    /// </summary>
    public KeyboardCharacterResolution? Resolved { get; init; }

    public bool IsPredictionTabAcceptance { get; init; }

    public bool IsPredictionEscDismissal { get; init; }

    /// <summary>
    /// True when the event was a genuine key event held by the replacement
    /// barrier and then replayed after the replacement batch completed. The
    /// target application has already received the replay; observers use this
    /// marker to reconcile their internal buffers without attempting another
    /// text transaction at the same boundary.
    /// </summary>
    public bool IsDeferredReplay { get; init; }
}
