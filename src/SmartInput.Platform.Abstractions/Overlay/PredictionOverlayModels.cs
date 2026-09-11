namespace SmartInput.Platform.Abstractions.Overlay;

public enum PredictionOverlayVisibility
{
    Hidden,
    Visible,
}

public sealed record PredictionOverlayContent(string SuggestionText);

public sealed record PredictionOverlayPlacement(
    int X,
    int Y,
    int CaretHeight);

public sealed record PredictionOverlayStatus(
    PredictionOverlayVisibility Visibility,
    bool IsWindowCreated);

public sealed record CaretScreenPosition(
    int X,
    int Y,
    int CaretWidth,
    int CaretHeight,
    nint WindowHandle);
