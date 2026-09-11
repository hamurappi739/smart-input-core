namespace SmartInput.Core.Models;

public sealed class LivePredictionOverlaySnapshot
{
    public required string SuggestionText { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public long Version { get; init; }
}
