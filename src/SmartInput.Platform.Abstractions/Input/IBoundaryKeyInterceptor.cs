namespace SmartInput.Platform.Abstractions.Input;

public sealed class BoundaryInterceptResult
{
    public bool ShouldSuppress { get; init; }

    public bool IsPhysicalKeyUpSuppression { get; init; }

    public DeferredBoundaryKey? Boundary { get; init; }

    /// <summary>
    /// An in-memory correction that was fully prepared before the boundary key
    /// was suppressed. It is never logged or persisted by the platform.
    /// </summary>
    public PreparedLayoutCorrection? PreparedCorrection { get; init; }

    /// <summary>
    /// Hook-thread token snapshot associated with this boundary. This is an
    /// in-memory handoff only; callers must not log or persist it.
    /// </summary>
    public string? CompletedToken { get; init; }

    /// <summary>
    /// Resolution produced on the hook thread. It is carried in memory so
    /// the asynchronous coordinator does not resolve the same physical event
    /// under a later keyboard layout.
    /// </summary>
    public KeyboardCharacterResolution? Resolution { get; init; }

    /// <summary>
    /// A correction prepared for the current character rather than for a
    /// word-boundary key. The character itself is still passed through; the
    /// coordinator replaces the already visible prefix atomically and the
    /// replacement barrier holds subsequent user input.
    /// </summary>
    public bool IsEarlyLayoutCorrection { get; init; }

    public static BoundaryInterceptResult PassThrough(
        string? completedToken = null,
        KeyboardCharacterResolution? resolution = null,
        PreparedLayoutCorrection? preparedCorrection = null,
        bool isEarlyLayoutCorrection = false)
    {
        return new BoundaryInterceptResult
        {
            CompletedToken = completedToken,
            Resolution = resolution,
            PreparedCorrection = preparedCorrection,
            IsEarlyLayoutCorrection = isEarlyLayoutCorrection,
        };
    }

    public static BoundaryInterceptResult Suppress(
        DeferredBoundaryKey boundary,
        PreparedLayoutCorrection? preparedCorrection = null,
        string? completedToken = null,
        KeyboardCharacterResolution? resolution = null)
    {
        return new BoundaryInterceptResult
        {
            ShouldSuppress = true,
            Boundary = boundary,
            PreparedCorrection = preparedCorrection,
            CompletedToken = completedToken,
            Resolution = resolution,
        };
    }

    public static BoundaryInterceptResult SuppressPhysicalKeyUp()
    {
        return new BoundaryInterceptResult
        {
            ShouldSuppress = true,
            IsPhysicalKeyUpSuppression = true,
        };
    }
}

public interface IBoundaryKeyInterceptor
{
    BoundaryInterceptResult TryIntercept(KeyboardObservationEventArgs observation, KeyboardHookMetadata metadata);

    /// <summary>
    /// True after hook-time preflight has identified an early layout switch.
    /// The monitor uses this to hold the next genuine key until C# finishes
    /// the existing replacement transaction.
    /// </summary>
    bool HasPendingEarlyLayoutCorrection() => false;
}

public interface ILiveLayoutBoundaryGate
{
    bool ShouldSuppressPendingBoundary();

    /// <summary>
    /// Observes the foreground window synchronously on the hook path. A
    /// window change must reset preflight before the first character of the
    /// new window is appended; waiting for the asynchronous coordinator can
    /// otherwise erase later hook-thread characters.
    /// </summary>
    void ObserveApplicationContext(nint windowHandle) { }

    /// <summary>
    /// Receives genuine KeyDown resolution on the hook thread. Returns a
    /// correction only at a supported word boundary and only when it was
    /// prepared from the already-observed token.
    /// </summary>
    PreparedLayoutCorrection? ObserveKeyDown(KeyboardCharacterResolution resolution) => null;

    /// <summary>
    /// Indicates that the most recent character produced an early correction
    /// which is being processed by the asynchronous coordinator.
    /// </summary>
    bool HasPendingEarlyLayoutCorrection() => false;

    /// <summary>
    /// Takes the complete token captured by hook-thread preflight for the most
    /// recent boundary. The value is an in-memory handoff only.
    /// </summary>
    string? TakeCompletedToken() => null;

    /// <summary>
    /// Drops the preflight token after focus, policy, or pause changes.
    /// </summary>
    void ResetPendingToken() { }
}

public sealed class PreparedLayoutCorrection
{
    public required string OriginalText { get; init; }

    public required string ReplacementText { get; init; }

    public KeyboardInputLanguage TargetInputLanguage { get; init; }
}
