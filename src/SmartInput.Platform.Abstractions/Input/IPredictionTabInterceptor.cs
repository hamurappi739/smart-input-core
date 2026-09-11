namespace SmartInput.Platform.Abstractions.Input;

public sealed class PredictionTabInterceptResult
{
    public bool ShouldSuppress { get; init; }

    public bool IsTabAcceptance { get; init; }

    public static PredictionTabInterceptResult PassThrough()
    {
        return new PredictionTabInterceptResult();
    }

    public static PredictionTabInterceptResult SuppressTabAcceptance()
    {
        return new PredictionTabInterceptResult
        {
            ShouldSuppress = true,
            IsTabAcceptance = true,
        };
    }
}

public interface IPredictionTabAcceptanceGate
{
    bool ShouldInterceptPlainTab();
}

public interface IPredictionTabInterceptor
{
    PredictionTabInterceptResult TryIntercept(
        KeyboardObservationEventArgs observation,
        KeyboardHookMetadata metadata);
}
