namespace SmartInput.Core.Configuration;

public sealed class CurrentTokenBufferOptions
{
    public const int DefaultMaxTokenLength = 48;

    public int MaxTokenLength { get; init; } = DefaultMaxTokenLength;
}
