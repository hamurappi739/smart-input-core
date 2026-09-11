namespace SmartInput.Core.Models;

public sealed class AutocorrectionResult
{
    public required string OriginalToken { get; init; }

    public string? CandidateToken { get; init; }

    public double ConfidenceScore { get; init; }

    /// <summary>
    /// True when the candidate was selected by the opted-in local spelling
    /// provider chain. The joint live gate can use this provenance for a
    /// narrowly bounded external-spelling rule without weakening the normal
    /// corpus safety oracle.
    /// </summary>
    public bool IsExternalProviderCandidate { get; init; }

    public AutocorrectionRecommendation Recommendation { get; init; }

    public TypingLanguage ActiveLanguage { get; init; }
}
