using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

/// <summary>
/// Local provider contract for punctuation-only inference. Implementations may
/// use an ONNX model later, but may not inspect process context, log input or
/// invoke text replacement APIs.
/// </summary>
public interface IPunctuationProvider
{
    IReadOnlyList<PunctuationProposal> Analyze(string normalizedText);
}

public interface IPunctuationPreviewService
{
    PunctuationPreviewResult CreatePreview(string text);

    /// <summary>
    /// Returns the exact pre-preview fragment. Applying a preview is deliberately
    /// outside this Core service; the UI can use this value to create one
    /// transaction in the existing undo pipeline.
    /// </summary>
    string? RevertPreview(PunctuationPreviewResult preview);
}

/// <summary>Default until an explicitly installed local punctuation model exists.</summary>
public sealed class NullPunctuationProvider : IPunctuationProvider
{
    public static NullPunctuationProvider Instance { get; } = new();

    private NullPunctuationProvider()
    {
    }

    public IReadOnlyList<PunctuationProposal> Analyze(string normalizedText) => [];
}
