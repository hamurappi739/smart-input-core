namespace SmartInput.Platform.Abstractions.Input;

public interface IBoundaryKeyDeliveryService
{
    Task DeliverAsync(DeferredBoundaryKey boundary, CancellationToken cancellationToken = default);
}
