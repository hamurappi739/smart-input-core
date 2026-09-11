using SmartInput.Core.Configuration;

namespace SmartInput.Core.Models;

public sealed class PredictionRequest
{
    public required string Context { get; init; }

    public required TypingLanguage ActiveLanguage { get; init; }

    public string? CurrentWordPrefix { get; init; }

    public PredictionOptions? Options { get; init; }
}
