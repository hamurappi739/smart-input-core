namespace SmartInput.Platform.Abstractions.Overlay;

public interface ICaretPositionService
{
    Task<CaretScreenPosition?> GetCaretScreenPositionAsync(CancellationToken cancellationToken = default);
}
