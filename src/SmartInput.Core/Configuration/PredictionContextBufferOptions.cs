namespace SmartInput.Core.Configuration;

public sealed class PredictionContextBufferOptions
{
    public const int DefaultMaxWords = 8;

    public const int DefaultMaxCharacters = 256;

    public int MaxWords { get; init; } = DefaultMaxWords;

    public int MaxCharacters { get; init; } = DefaultMaxCharacters;
}
