namespace SmartInput.Core.Models;

public sealed class PredictionTabAcceptanceAttempt
{
    public required string WordPrefix { get; init; }

    public required string SuggestionText { get; init; }

    public long Version { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
