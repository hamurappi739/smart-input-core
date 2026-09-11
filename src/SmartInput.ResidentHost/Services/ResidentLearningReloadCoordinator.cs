using Microsoft.Extensions.Logging;
using SmartInput.Core.Persistence;
using SmartInput.Infrastructure.Persistence;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Synchronizes explicit "Allow again" actions from the UI-only settings
/// process. It reloads only local rejection metadata; no typed text is logged.
/// </summary>
internal sealed class ResidentLearningReloadCoordinator : IAsyncDisposable
{
    private readonly ICorrectionRejectionLearningStore _learningStore;
    private readonly ILogger<ResidentLearningReloadCoordinator> _logger;
    private readonly string _learningPath;
    private readonly object _taskSync = new();
    private readonly HashSet<Task> _reloadTasks = [];
    private FileSystemWatcher? _watcher;
    private int _reloadQueued;
    private CancellationTokenSource? _pendingReload;
    private int _disposed;

    public ResidentLearningReloadCoordinator(
        ICorrectionRejectionLearningStore learningStore,
        ILogger<ResidentLearningReloadCoordinator> logger)
    {
        _learningStore = learningStore;
        _logger = logger;
        _learningPath = JsonCorrectionRejectionLearningPersistence.GetDefaultLearningPath();
    }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0 || _watcher is not null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_learningPath);
        var fileName = Path.GetFileName(_learningPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnLearningFileChanged;
        _watcher.Created += OnLearningFileChanged;
        _watcher.Renamed += OnLearningFileRenamed;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnLearningFileChanged;
            _watcher.Created -= OnLearningFileChanged;
            _watcher.Renamed -= OnLearningFileRenamed;
            _watcher.Dispose();
        }

        var pending = Interlocked.Exchange(ref _pendingReload, null);
        CancelSafely(pending);

        Task[] activeTasks;
        lock (_taskSync)
        {
            activeTasks = _reloadTasks.ToArray();
        }

        if (activeTasks.Length > 0)
        {
            await Task.WhenAll(activeTasks).ConfigureAwait(false);
        }
    }

    private static void CancelSafely(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The reload completed between the exchange and cancellation.
        }
    }

    private void OnLearningFileChanged(object sender, FileSystemEventArgs e) => QueueReload();

    private void OnLearningFileRenamed(object sender, RenamedEventArgs e) => QueueReload();

    private void QueueReload()
    {
        var request = new CancellationTokenSource();
        Task task;
        lock (_taskSync)
        {
            if (Volatile.Read(ref _disposed) != 0
                || Interlocked.CompareExchange(ref _reloadQueued, 1, 0) != 0)
            {
                request.Dispose();
                return;
            }

            Interlocked.Exchange(ref _pendingReload, request);
            task = ReloadAfterWriteSettlesAsync(request);
            _reloadTasks.Add(task);
        }

        _ = ObserveReloadTaskAsync(task);
    }

    private async Task ObserveReloadTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_taskSync)
            {
                _reloadTasks.Remove(task);
            }
        }
    }

    private async Task ReloadAfterWriteSettlesAsync(CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), request.Token).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) == 0
                && !request.IsCancellationRequested
                && File.Exists(_learningPath))
            {
                await _learningStore.ReloadAsync(request.Token).ConfigureAwait(false);
                _logger.LogInformation("Resident correction-learning metadata reloaded after settings update.");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a superseding lifecycle event cancelled the reload.
        }
        catch (IOException)
        {
            // A following watcher event retries after a short concurrent save.
        }
        catch (UnauthorizedAccessException)
        {
            // Treat a temporary editor lock as an unavailable local file.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resident correction-learning reload failed safely.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingReload, null, request);
            request.Dispose();
            Volatile.Write(ref _reloadQueued, 0);
        }
    }
}
