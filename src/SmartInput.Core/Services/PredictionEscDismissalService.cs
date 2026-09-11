namespace SmartInput.Core.Services;

public interface IPredictionEscDismissalService
{
    bool DismissVisibleSuggestion();

    bool IsDismissedForCurrentContext { get; }
}

public sealed class PredictionEscDismissalService : IPredictionEscDismissalService
{
    private readonly ILivePredictionEngine _predictionEngine;

    public PredictionEscDismissalService(ILivePredictionEngine predictionEngine)
    {
        _predictionEngine = predictionEngine;
    }

    public bool IsDismissedForCurrentContext => _predictionEngine.IsOverlayDismissedForCurrentContext;

    public bool DismissVisibleSuggestion()
    {
        return _predictionEngine.TryDismissOverlaySuggestion();
    }
}
