using SmartInput.Core.Engines;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Models;

public enum JointCorrectionRecommendation
{
    NoChange,
    Wait,
    Apply,
}

public sealed class JointCorrectionDecisionResult
{
    public string OriginalToken { get; init; } = string.Empty;

    public string? ReplacementToken { get; init; }

    public JointCorrectionRecommendation Recommendation { get; init; }

    public CorrectionKind Kind { get; init; }

    public double ConfidenceScore { get; init; }

    public LayoutConversionDirection? LayoutDirection { get; init; }

    public KeyboardInputLanguage? TargetInputLanguage { get; init; }

    public static JointCorrectionDecisionResult NoChange(string token)
    {
        return new JointCorrectionDecisionResult
        {
            OriginalToken = token,
            Recommendation = JointCorrectionRecommendation.NoChange,
        };
    }

    public static JointCorrectionDecisionResult Wait(string token)
    {
        return new JointCorrectionDecisionResult
        {
            OriginalToken = token,
            Recommendation = JointCorrectionRecommendation.Wait,
        };
    }
}
