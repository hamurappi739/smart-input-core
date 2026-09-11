namespace SmartInput.Platform.Abstractions.Input;

public sealed class PredictionEscInterceptResult
{
    public bool ShouldSuppress { get; init; }

    public bool IsEscDismissal { get; init; }

    public static PredictionEscInterceptResult PassThrough()
    {
        return new PredictionEscInterceptResult();
    }

    public static PredictionEscInterceptResult SuppressEscDismissal()
    {
        return new PredictionEscInterceptResult
        {
            ShouldSuppress = true,
            IsEscDismissal = true,
        };
    }
}

public interface IPredictionEscDismissalGate
{
    bool ShouldInterceptPlainEsc();
}

public interface IPredictionEscInterceptor
{
    PredictionEscInterceptResult TryIntercept(
        KeyboardObservationEventArgs observation,
        KeyboardHookMetadata metadata);
}
