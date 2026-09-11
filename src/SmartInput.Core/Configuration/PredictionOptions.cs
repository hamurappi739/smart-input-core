namespace SmartInput.Core.Configuration;

public sealed class PredictionOptions
{
    public const int DefaultMaxSuggestionTokens = 3;

    public const int DefaultMaxSuggestionCharacters = 24;

    public const int DefaultSuggestionExpirySeconds = 30;

    public const int DefaultMinContextWords = 1;

    public const int DefaultMinContextCharacters = 2;

    public const int DefaultMaxContextWords = 8;

    public const double DefaultSuggestionConfidenceThreshold = 0.55;

    public const double DefaultLowConfidenceThreshold = 0.35;

    public int MaxSuggestionTokens { get; init; } = DefaultMaxSuggestionTokens;

    public int MaxSuggestionCharacters { get; init; } = DefaultMaxSuggestionCharacters;

    public int MinContextWords { get; init; } = DefaultMinContextWords;

    public int MinContextCharacters { get; init; } = DefaultMinContextCharacters;

    public int MaxContextWords { get; init; } = DefaultMaxContextWords;

    public double SuggestionConfidenceThreshold { get; init; } = DefaultSuggestionConfidenceThreshold;

    public double LowConfidenceThreshold { get; init; } = DefaultLowConfidenceThreshold;
}
