using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.App.Services;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.ResidentHost.Services;

namespace SmartInput.ResidentHost.Tests;

public sealed class ResidentSnippetReloadCoordinatorTests
{
    [Fact]
    public async Task SnippetFileChange_ReloadsResidentSnapshotAndResetsLiveBuffer()
    {
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "snippets.json");

        try
        {
            var persistence = CreatePersistence(filePath);
            await persistence.SaveAsync([CreateSnippet("initial", "one")]);

            var service = new SnippetService(persistence);
            await service.LoadAsync();
            var layout = new TrackingLayoutCoordinator();
            await using var coordinator = new ResidentSnippetReloadCoordinator(
                service,
                layout,
                NullLogger<ResidentSnippetReloadCoordinator>.Instance,
                filePath);

            coordinator.Start();
            await persistence.SaveAsync([CreateSnippet("updated", "two")]);

            await WaitUntilAsync(
                () => service.FindMatch("updated").IsMatch,
                TimeSpan.FromSeconds(4));

            Assert.False(service.FindMatch("initial").IsMatch);
            Assert.Equal("two", service.FindMatch("updated").Replacement);
            Assert.Equal(1, layout.ResetCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DisposeAsync_StopsFutureReloads()
    {
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "snippets.json");

        try
        {
            var persistence = CreatePersistence(filePath);
            await persistence.SaveAsync([CreateSnippet("initial", "one")]);
            var service = new SnippetService(persistence);
            await service.LoadAsync();
            var layout = new TrackingLayoutCoordinator();
            var coordinator = new ResidentSnippetReloadCoordinator(
                service,
                layout,
                NullLogger<ResidentSnippetReloadCoordinator>.Instance,
                filePath);

            coordinator.Start();
            await coordinator.DisposeAsync();
            await persistence.SaveAsync([CreateSnippet("after", "two")]);
            await Task.Delay(500);

            Assert.False(service.FindMatch("after").IsMatch);
            Assert.Equal(0, layout.ResetCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static JsonSnippetPersistence CreatePersistence(string path)
        => new(path, NullLogger<JsonSnippetPersistence>.Instance);

    private static SnippetDefinition CreateSnippet(string trigger, string replacement)
        => new()
        {
            Trigger = trigger,
            Replacement = replacement,
            IsEnabled = true,
        };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The snippet watcher did not reload within the test timeout.");
            }

            await Task.Delay(25);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"smartinput-snippet-resident-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TrackingLayoutCoordinator : ILiveLayoutCorrectionCoordinator
    {
        public LiveLayoutCorrectionStatus Status => new();

        public int ResetCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void ResetBuffer() => ResetCount++;
    }
}
