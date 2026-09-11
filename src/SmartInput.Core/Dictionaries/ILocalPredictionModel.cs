using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

public sealed class PredictionModelCandidate
{
    public required string Token { get; init; }

    public double FrequencyScore { get; init; }
}

public interface ILocalPredictionModel
{
    IReadOnlyList<PredictionModelCandidate> GetCandidates(
        TypingLanguage language,
        IReadOnlyList<string> contextTokens,
        string? currentWordPrefix);
}
