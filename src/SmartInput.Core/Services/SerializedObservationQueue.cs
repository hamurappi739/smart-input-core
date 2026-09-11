namespace SmartInput.Core.Services;

/// <summary>
/// Keeps asynchronous input observations in submission order without making
/// the low-level hook thread wait for policy, dictionary, or replacement I/O.
/// </summary>
public sealed class SerializedObservationQueue
{
    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_sync)
        {
            _tail = _tail
                .ContinueWith(
                    _ => operation(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();

            return _tail;
        }
    }
}
