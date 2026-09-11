namespace SmartInput.Platform.Abstractions.Overlay;

public interface IPredictionOverlayService
{
    PredictionOverlayStatus Status { get; }

    Task ShowAsync(
        PredictionOverlayContent content,
        PredictionOverlayPlacement placement,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        PredictionOverlayContent content,
        PredictionOverlayPlacement placement,
        CancellationToken cancellationToken = default);

    Task HideAsync(CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
