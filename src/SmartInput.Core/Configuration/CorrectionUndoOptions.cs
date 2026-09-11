namespace SmartInput.Core.Configuration;

public sealed class CorrectionUndoOptions
{
    public const int DefaultTransactionTimeoutSeconds = 30;

    public int TransactionTimeoutSeconds { get; init; } = DefaultTransactionTimeoutSeconds;

    public int ReplacementTimeoutMilliseconds { get; init; } =
        LayoutCorrectionOptions.DefaultReplacementTimeoutMilliseconds;
}
