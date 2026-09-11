using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.App.Services;

namespace SmartInput.App.Tests;

public class ApplicationShutdownCoordinatorTests
{
    [Fact]
    public async Task PerformCleanupAsync_RunsExactlyOnce()
    {
        var tray = new RecordingTrayCoordinator();
        var services = new ServiceCollection();
        services.AddSingleton<ISystemTrayCoordinator>(tray);

        var provider = services.BuildServiceProvider();
        var coordinator = new ApplicationShutdownCoordinator(
            provider,
            NullLogger<ApplicationShutdownCoordinator>.Instance);

        await coordinator.PerformCleanupAsync();
        await coordinator.PerformCleanupAsync();

        Assert.True(coordinator.CleanupPerformed);
        Assert.Equal(1, tray.DisposeCallCount);
    }

    private sealed class RecordingTrayCoordinator : ISystemTrayCoordinator, IAsyncDisposable
    {
        public int DisposeCallCount { get; private set; }

        public bool IsTrayAvailable => true;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

}
