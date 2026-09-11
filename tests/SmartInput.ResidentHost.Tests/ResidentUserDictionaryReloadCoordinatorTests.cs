using Microsoft.Extensions.Logging.Abstractions;
using SmartInput.App.Services;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Infrastructure.Persistence;
using SmartInput.ResidentHost.Services;

namespace SmartInput.ResidentHost.Tests;

public sealed class ResidentUserDictionaryReloadCoordinatorTests
{
    [Fact]
    public async Task AtomicDictionaryReplacement_ReloadsStoreAndResetsLiveBuffer()
    {
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "user-autocorrect-dictionary.json");

        try
        {
            var persistence = new JsonUserAutocorrectDictionaryPersistence(
                filePath,
                NullLogger<JsonUserAutocorrectDictionaryPersistence>.Instance);
            await persistence.SaveAsync(
            [
                new UserAutocorrectDictionaryEntry
                {
                    Word = "initial",
                    Language = TypingLanguage.English,
                },
            ]);

            var store = new TrackingUserDictionaryStore(persistence);
            await store.LoadAsync();
            var layout = new TrackingLayoutCoordinator();
            await using var coordinator = new ResidentUserDictionaryReloadCoordinator(
                store,
                layout,
                NullLogger<ResidentUserDictionaryReloadCoordinator>.Instance,
                filePath);

            coordinator.Start();
            await persistence.SaveAsync(
            [
                new UserAutocorrectDictionaryEntry
                {
                    Word = "reloaded",
                    Language = TypingLanguage.English,
                },
            ]);

            await WaitUntilAsync(
                () => store.LoadCount >= 2,
                TimeSpan.FromSeconds(4));

            Assert.Single(store.Entries);
            Assert.Equal("reloaded", store.Entries[0].Word);
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
        var filePath = Path.Combine(directory, "user-autocorrect-dictionary.json");

        try
        {
            var persistence = new JsonUserAutocorrectDictionaryPersistence(
                filePath,
                NullLogger<JsonUserAutocorrectDictionaryPersistence>.Instance);
            await persistence.SaveAsync([]);

            var store = new TrackingUserDictionaryStore(persistence);
            await store.LoadAsync();
            var layout = new TrackingLayoutCoordinator();
            var coordinator = new ResidentUserDictionaryReloadCoordinator(
                store,
                layout,
                NullLogger<ResidentUserDictionaryReloadCoordinator>.Instance,
                filePath);

            coordinator.Start();
            await coordinator.DisposeAsync();
            var loadCountAfterDispose = store.LoadCount;

            await persistence.SaveAsync(
            [
                new UserAutocorrectDictionaryEntry
                {
                    Word = "after-dispose",
                    Language = TypingLanguage.English,
                },
            ]);
            await Task.Delay(500);

            Assert.Equal(loadCountAfterDispose, store.LoadCount);
            Assert.Equal(0, layout.ResetCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task DisposeAsync_WaitsForInFlightReload_AndDoesNotResetAfterShutdown()
    {
        var directory = CreateTemporaryDirectory();
        var filePath = Path.Combine(directory, "user-autocorrect-dictionary.json");

        try
        {
            await File.WriteAllTextAsync(filePath, "{}");
            var store = new BlockingUserDictionaryStore();
            var layout = new TrackingLayoutCoordinator();
            var coordinator = new ResidentUserDictionaryReloadCoordinator(
                store,
                layout,
                NullLogger<ResidentUserDictionaryReloadCoordinator>.Instance,
                filePath);

            coordinator.Start();
            await File.WriteAllTextAsync(filePath, "{\"entries\":[]}");
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));

            var disposeTask = coordinator.DisposeAsync().AsTask();
            Assert.False(disposeTask.IsCompleted);

            store.Release.TrySetResult();
            await disposeTask;

            Assert.Equal(0, layout.ResetCount);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The file watcher did not reload within the test timeout.");
            }

            await Task.Delay(25);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"smartinput-resident-test-{Guid.NewGuid():N}");
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

    private sealed class TrackingUserDictionaryStore(
        JsonUserAutocorrectDictionaryPersistence persistence) : IUserAutocorrectDictionaryStore
    {
        private readonly JsonUserAutocorrectDictionaryPersistence _persistence = persistence;

        public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries { get; private set; } = [];

        public int LoadCount { get; private set; }

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            Entries = (await _persistence.LoadAsync(cancellationToken)).ToList();
            LoadCount++;
        }

        public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class BlockingUserDictionaryStore : IUserAutocorrectDictionaryStore
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries { get; private set; } = [];

        public int LoadCount { get; private set; }

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            LoadCount++;
        }

        public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
