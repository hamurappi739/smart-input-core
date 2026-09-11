namespace SmartInput.Core.Configuration;

public sealed class LayoutCorrectionOptions
{
    public const int DefaultReplacementTimeoutMilliseconds = 3000;

    public int ReplacementTimeoutMilliseconds { get; init; } = DefaultReplacementTimeoutMilliseconds;
}
