using Microsoft.Extensions.Logging;
using SmartInput.App.Services;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Keeps the resident process synchronized with snippets saved by the
/// separate settings process. The resident host must not depend on the UI
/// process being restarted after a snippet is created or edited.
/// </summary>
public sealed class ResidentSnippetReloadCoordinator : IAsyncDisposable
{
    private readonly ISnippetService _snippetService;
    private readonly ILiveLayoutCorrectionCoordinator _layoutCoordinator;
    private readonly ILogger<ResidentSnippetReloadCoordinator> _logger;
    private readonly string _snippetsPath;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly object _taskSync = new();
    private readonly HashSet<Task> _reloadTasks = [];

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pendingReload;
    private bool _started;
    private int _disposed;

    public ResidentSnippetReloadCoordinator(
        ISnippetService snippetService,
        ILiveLayoutCorrectionCoordinator layoutCoordinator,
        ILogger<ResidentSnippetReloadCoordinator> logger)
        : this(
            snippetService,
            layoutCoordinator,
            logger,
            JsonSnippetPersistence.GetDefaultSnippetsPath())
    {
    }

    public ResidentSnippetReloadCoordinator(
        ISnippetService snippetService,
        ILiveLayoutCorrectionCoordinator layoutCoordinator,
        ILogger<ResidentSnippetReloadCoordinator> logger,
        string snippetsPath)
    {
        _snippetService = snippetService;
        _layoutCoordinator = layoutCoordinator;
        _logger = logger;
        _snippetsPath = snippetsPath;
    }

    public void Start()
    {
        if (_started || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_snippetsPath);
        var fileName = Path.GetFileName(_snippetsPath);
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
        _watcher.Changed += OnSnippetsFileChanged;
        _watcher.Created += OnSnippetsFileChanged;
        _watcher.Renamed += OnSnippetsFileRenamed;
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
            _watcher.Changed -= OnSnippetsFileChanged;
            _watcher.Created -= OnSnippetsFileChanged;
            _watcher.Renamed -= OnSnippetsFileRenamed;
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
    }

    private void OnSnippetsFileChanged(object sender, FileSystemEventArgs e) => QueueReload();

    private void OnSnippetsFileRenamed(object sender, RenamedEventArgs e) => QueueReload();

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
                    && File.Exists(_snippetsPath))
                {
                    await _snippetService.LoadAsync(request.Token).ConfigureAwait(false);
                    if (Volatile.Read(ref _disposed) == 0
                        && !request.IsCancellationRequested)
                    {
                        // A pending token may have been evaluated against the
                        // previous snapshot. Drop it before accepting new input.
                        _layoutCoordinator.ResetBuffer();
                        _logger.LogInformation("Resident snippets reloaded after settings window update.");
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
        catch (IOException)
        {
            // Save writes are short-lived; a later event retries the reload.
        }
        catch (UnauthorizedAccessException)
        {
            // Treat a concurrent editor lock as a transient unavailable file.
        }
        catch (Exception ex)
        {
            // A malformed snippet file must not stop the resident hook.
            _logger.LogWarning(ex, "Resident snippets reload failed safely.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingReload, null, request);
            request.Dispose();
        }
    }
}
