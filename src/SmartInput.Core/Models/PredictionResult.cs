namespace SmartInput.Core.Models;

public sealed class PredictionResult
{
    public required string Context { get; init; }

    public string? SuggestedContinuation { get; init; }

    public double Confidence { get; init; }

    public int TokenCount { get; init; }

    public PredictionRecommendation Recommendation { get; init; }

    public TypingLanguage ActiveLanguage { get; init; }

    public static PredictionResult NoSuggestion(string context, TypingLanguage activeLanguage)
    {
        return new PredictionResult
        {
            Context = context,
            SuggestedContinuation = null,
            Confidence = 0.0,
            TokenCount = 0,
            Recommendation = PredictionRecommendation.NoSuggestion,
            ActiveLanguage = activeLanguage,
        };
    }
}
