namespace SmartInput.Core.Tests;

public sealed class SerializedObservationQueueTests
{
    [Fact]
    public async Task Enqueue_PreservesSubmissionOrder_WhenEarlierOperationAwaits()
    {
        var queue = new SmartInput.Core.Services.SerializedObservationQueue();
        var releaseFirst = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<int>();

        var first = queue.Enqueue(async () =>
        {
            events.Add(1);
            await releaseFirst.Task;
            events.Add(2);
        });

        var second = queue.Enqueue(() =>
        {
            events.Add(3);
            return Task.CompletedTask;
        });

        await Task.Delay(25);
        Assert.Equal([1], events);

        releaseFirst.SetResult(true);
        await Task.WhenAll(first, second);

        Assert.Equal([1, 2, 3], events);
    }

    [Fact]
    public async Task Enqueue_ContinuesAfterAnOperationFails()
    {
        var queue = new SmartInput.Core.Services.SerializedObservationQueue();
        var secondRan = false;

        var first = queue.Enqueue(() => Task.FromException(
            new InvalidOperationException("synthetic")));
        var second = queue.Enqueue(() =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await second;

        Assert.True(secondRan);
    }
}
