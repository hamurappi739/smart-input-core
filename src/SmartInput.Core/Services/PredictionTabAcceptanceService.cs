using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public interface IPredictionTabAcceptanceService
{
    bool TryBeginAcceptance(out PredictionTabAcceptanceAttempt attempt);

    void CompleteAcceptance(long version);

    void AbortAcceptance(long version);
}

public sealed class PredictionTabAcceptanceService : IPredictionTabAcceptanceService
{
    private readonly ILivePredictionEngine _predictionEngine;

    public PredictionTabAcceptanceService(ILivePredictionEngine predictionEngine)
    {
        _predictionEngine = predictionEngine;
    }

    public bool TryBeginAcceptance(out PredictionTabAcceptanceAttempt attempt)
    {
        return _predictionEngine.TryBeginTabAcceptance(out attempt);
    }

    public void CompleteAcceptance(long version)
    {
        _predictionEngine.CompleteTabAcceptance(version);
    }

    public void AbortAcceptance(long version)
    {
        _predictionEngine.AbortTabAcceptance(version);
    }
}
