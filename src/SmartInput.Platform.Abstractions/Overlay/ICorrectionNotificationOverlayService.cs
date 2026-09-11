namespace SmartInput.Platform.Abstractions.Overlay;

public enum CorrectionNotificationVisibility
{
    Hidden,
    Visible,
}

public sealed record CorrectionNotificationContent(string Message);

public sealed record CorrectionNotificationStatus(
    CorrectionNotificationVisibility Visibility,
    bool IsWindowCreated);

public interface ICorrectionNotificationOverlayService
{
    CorrectionNotificationStatus Status { get; }

    Task ShowAsync(
        CorrectionNotificationContent content,
        PredictionOverlayPlacement placement,
        CancellationToken cancellationToken = default);

    Task HideAsync(CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
