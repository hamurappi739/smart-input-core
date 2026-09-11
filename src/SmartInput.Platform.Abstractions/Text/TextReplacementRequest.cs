namespace SmartInput.Platform.Abstractions.Text;

public sealed class TextReplacementRequest
{
    public required string OriginalText { get; init; }

    public required string ReplacementText { get; init; }
}
