namespace SmartInput.Platform.Abstractions.Input;

public interface IInputMonitor
{
    bool IsMonitoring { get; }

    event EventHandler<KeyboardObservationEventArgs>? InputObserved;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
