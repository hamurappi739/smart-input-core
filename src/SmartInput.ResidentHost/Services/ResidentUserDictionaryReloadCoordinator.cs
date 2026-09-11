using Microsoft.Extensions.Logging;
using SmartInput.App.Services;
using SmartInput.Core.Dictionaries;
using SmartInput.Infrastructure.Persistence;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Reloads the local user dictionary written by the separate settings
/// process. Reloading replaces the store snapshot in memory and resets the
/// live token/preflight state so a candidate created with an older dictionary
/// cannot cross the reload boundary.
/// </summary>
public sealed class ResidentUserDictionaryReloadCoordinator : IAsyncDisposable
{
    private readonly IUserAutocorrectDictionaryStore _dictionaryStore;
    private readonly ILiveLayoutCorrectionCoordinator _layoutCoordinator;
    private readonly ILogger<ResidentUserDictionaryReloadCoordinator> _logger;
    private readonly string _dictionaryPath;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly object _taskSync = new();
    private readonly HashSet<Task> _reloadTasks = [];

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pendingReload;
    private bool _started;
    private int _disposed;

    public ResidentUserDictionaryReloadCoordinator(
        IUserAutocorrectDictionaryStore dictionaryStore,
        ILiveLayoutCorrectionCoordinator layoutCoordinator,
        ILogger<ResidentUserDictionaryReloadCoordinator> logger)
        : this(
            dictionaryStore,
            layoutCoordinator,
            logger,
            JsonUserAutocorrectDictionaryPersistence.GetDefaultDictionaryPath())
    {
    }

    public ResidentUserDictionaryReloadCoordinator(
        IUserAutocorrectDictionaryStore dictionaryStore,
        ILiveLayoutCorrectionCoordinator layoutCoordinator,
        ILogger<ResidentUserDictionaryReloadCoordinator> logger,
        string dictionaryPath)
    {
        _dictionaryStore = dictionaryStore;
        _layoutCoordinator = layoutCoordinator;
        _logger = logger;
        _dictionaryPath = dictionaryPath;
    }

    public void Start()
    {
        if (_started || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_dictionaryPath);
        var fileName = Path.GetFileName(_dictionaryPath);
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
        _watcher.Changed += OnDictionaryFileChanged;
        _watcher.Created += OnDictionaryFileChanged;
        _watcher.Renamed += OnDictionaryFileRenamed;
        _started = true;
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
            _watcher.Changed -= OnDictionaryFileChanged;
            _watcher.Created -= OnDictionaryFileChanged;
            _watcher.Renamed -= OnDictionaryFileRenamed;
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

        _reloadGate.Dispose();
        pending?.Dispose();
    }

    private void OnDictionaryFileChanged(object sender, FileSystemEventArgs e) => QueueReload();

    private void OnDictionaryFileRenamed(object sender, RenamedEventArgs e) => QueueReload();

    private void QueueReload()
    {
        var request = new CancellationTokenSource();
        CancellationTokenSource? previous;
        Task task;
        lock (_taskSync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                request.Dispose();
                return;
            }

            previous = Interlocked.Exchange(ref _pendingReload, request);
            task = ReloadAfterWriteSettlesAsync(request);
            _reloadTasks.Add(task);
        }

        CancelSafely(previous);
        _ = ObserveReloadTaskAsync(task);
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
            // The previous reload completed between replacement and cancel.
        }
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
            await _reloadGate.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _disposed) == 0
                    && !request.IsCancellationRequested
                    && File.Exists(_dictionaryPath))
                {
                    await _dictionaryStore.LoadAsync(request.Token).ConfigureAwait(false);
                    if (Volatile.Read(ref _disposed) == 0
                        && !request.IsCancellationRequested)
                    {
                        _layoutCoordinator.ResetBuffer();
                        _logger.LogInformation("Resident user dictionary reloaded after settings window update.");
                    }
                }
            }
            finally
            {
                _reloadGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A subsequent filesystem event superseded this request.
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to reload resident user dictionary from local storage.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Resident user dictionary was temporarily unavailable.");
        }
        catch (Exception ex)
        {
            // A malformed or unexpected local file must not stop the resident
            // hook. Persistence itself already converts malformed JSON to an
            // empty safe snapshot.
            _logger.LogWarning(ex, "Resident user dictionary reload failed safely.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingReload, null, request);
            request.Dispose();
        }
    }
}
