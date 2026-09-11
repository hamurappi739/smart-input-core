namespace SmartInput.Core.Models;

public sealed class CorrectionTransaction
{
    public required string OriginalToken { get; init; }

    public required string ReplacementToken { get; init; }

    public required CorrectionKind Kind { get; init; }

    /// <summary>
    /// A printable boundary that Smart Input injected after the correction.
    /// It stays in memory only and lets undo preserve the user's trailing
    /// space or punctuation when the caret is already after that boundary.
    /// </summary>
    public string TrailingText { get; init; } = string.Empty;

    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? ApplicationProcessName { get; init; }

    public nint ApplicationWindowHandle { get; init; }
}
