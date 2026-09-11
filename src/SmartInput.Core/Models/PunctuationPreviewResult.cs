namespace SmartInput.Core.Models;

public enum PunctuationPreviewStatus
{
    NoChange,
    PreviewReady,
    UnsafeInput,
    ExistingPunctuation,
    InputTooLong,
}

/// <summary>
/// A punctuation-only proposal placed after a token. It never authorizes a
/// replacement by itself; the preview service validates and filters it.
/// </summary>
public sealed record PunctuationProposal(
    int AfterTokenIndex,
    char Mark,
    double Probability,
    string ProviderVersion);

/// <summary>One reversible insertion expressed against the original text.</summary>
public sealed record PunctuationPreviewEdit(int OriginalTextIndex, char Mark);

public sealed class PunctuationPreviewResult
{
    public static PunctuationPreviewResult NoChange { get; } = new()
    {
        Status = PunctuationPreviewStatus.NoChange,
    };

    public PunctuationPreviewStatus Status { get; init; }
    public string OriginalText { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public string? ProviderVersion { get; init; }
    public IReadOnlyList<PunctuationPreviewEdit> Edits { get; init; } = [];
}
