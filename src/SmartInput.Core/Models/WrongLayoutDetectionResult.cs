using SmartInput.Core.Engines;

namespace SmartInput.Core.Models;

public sealed class WrongLayoutDetectionResult
{
    public required string OriginalToken { get; init; }

    public string? CandidateToken { get; init; }

    public LayoutConversionDirection? ConversionDirection { get; init; }

    public double ConfidenceScore { get; init; }

    public LayoutDetectionRecommendation Recommendation { get; init; }
}
