namespace SmartInput.Core.Configuration;

public sealed class SnippetTriggerBufferOptions
{
    public const int DefaultMaxTriggerLength = 64;

    public int MaxTriggerLength { get; init; } = DefaultMaxTriggerLength;
}
