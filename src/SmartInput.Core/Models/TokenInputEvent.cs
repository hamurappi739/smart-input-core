using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Models;

public enum TokenInputKind
{
    Character,
    WordBoundary,
    Backspace,
    Reset,
    Uncertain,
}

public sealed class TokenInputEvent
{
    public TokenInputKind Kind { get; init; }

    public char Character { get; init; }

    public DeferredBoundaryKey? DeferredBoundary { get; init; }

    public PreparedLayoutCorrection? PreparedLayoutCorrection { get; init; }

    /// <summary>
    /// Marks a hook-prepared correction for the current character. The
    /// character has already reached the target and must be included in the
    /// token buffer before the prefix replacement runs.
    /// </summary>
    public bool IsEarlyLayoutCorrection { get; init; }

    /// <summary>
    /// Optional token captured synchronously by the platform boundary gate.
    /// This lets the engine reconcile an asynchronous event queue without
    /// relying on a possibly shorter rolling buffer.
    /// </summary>
    public string? CompletedToken { get; init; }

    /// <summary>
    /// True only when the platform deferred the original boundary key. Automatic
    /// replacement is unsafe after a boundary has already reached the target.
    /// </summary>
    public bool AllowsTextReplacementAtBoundary { get; init; }

    public static TokenInputEvent CharacterInput(
        char character,
        PreparedLayoutCorrection? preparedLayoutCorrection = null,
        bool isEarlyLayoutCorrection = false)
    {
        return new TokenInputEvent
        {
            Kind = TokenInputKind.Character,
            Character = character,
            PreparedLayoutCorrection = preparedLayoutCorrection,
            IsEarlyLayoutCorrection = isEarlyLayoutCorrection,
        };
    }

    /// <summary>
    /// Creates a synthetic boundary for callers that already control the text
    /// transaction, such as unit tests and explicit in-app scenarios.
    /// </summary>
    public static TokenInputEvent Boundary()
    {
        return new TokenInputEvent
        {
            Kind = TokenInputKind.WordBoundary,
            AllowsTextReplacementAtBoundary = true,
        };
    }

    /// <summary>
    /// Creates a boundary observed from the platform input stream. A replacement
    /// is safe only when the platform actually deferred the key.
    /// </summary>
    public static TokenInputEvent Boundary(
        DeferredBoundaryKey? deferredBoundary,
        PreparedLayoutCorrection? preparedLayoutCorrection = null,
        string? completedToken = null)
    {
        return new TokenInputEvent
        {
            Kind = TokenInputKind.WordBoundary,
            DeferredBoundary = deferredBoundary,
            PreparedLayoutCorrection = preparedLayoutCorrection,
            CompletedToken = completedToken,
            AllowsTextReplacementAtBoundary = deferredBoundary is not null,
        };
    }

    public static TokenInputEvent Backspace { get; } = new() { Kind = TokenInputKind.Backspace };

    public static TokenInputEvent Reset { get; } = new() { Kind = TokenInputKind.Reset };

    public static TokenInputEvent Uncertain { get; } = new() { Kind = TokenInputKind.Uncertain };
}
