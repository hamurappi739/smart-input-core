namespace SmartInput.Core.Models;

public enum PunctuationCorrectionRecommendation
{
    NoChange,
    Apply,
}

/// <summary>
/// Result of evaluating whether a single trailing space should be removed
/// immediately before an incoming punctuation character.
/// </summary>
public sealed class PunctuationCorrectionResult
{
    public static PunctuationCorrectionResult NoChange { get; } = new()
    {
        Recommendation = PunctuationCorrectionRecommendation.NoChange,
    };

    public PunctuationCorrectionRecommendation Recommendation { get; init; }

    /// <summary>Text removed from the document (typically one ASCII space).</summary>
    public string OriginalSegment { get; init; } = string.Empty;

    /// <summary>Replacement text (empty when only deleting the accidental space).</summary>
    public string ReplacementSegment { get; init; } = string.Empty;

    public char PunctuationCharacter { get; init; }
}
