using Microsoft.Extensions.Logging;
using SmartInput.Core.Engines;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Keeps the no-UI resident process in sync when the separately opened
/// settings process writes settings.json. A debounced watcher avoids holding
/// the file open and retries a partial write on the next change notification.
/// </summary>
internal sealed class ResidentSettingsReloadCoordinator : IAsyncDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly SymSpellSpellCorrectionProvider? _symSpell;
    private readonly ILogger<ResidentSettingsReloadCoordinator> _logger;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly object _taskSync = new();
    private readonly HashSet<Task> _reloadTasks = [];

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pendingReload;
    private bool _started;
    private int _disposed;

    public ResidentSettingsReloadCoordinator(
        ISettingsService settingsService,
        ILogger<ResidentSettingsReloadCoordinator> logger,
        SymSpellSpellCorrectionProvider? symSpell = null)
    {
        _settingsService = settingsService;
        _logger = logger;
        _symSpell = symSpell;
        _settingsPath = JsonSettingsPersistence.GetDefaultSettingsPath();
    }

    public void Start()
    {
        if (_started || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_settingsPath);
        var fileName = Path.GetFileName(_settingsPath);
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
        _watcher.Changed += OnSettingsFileChanged;
        _watcher.Created += OnSettingsFileChanged;
        _watcher.Renamed += OnSettingsFileRenamed;
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
            _watcher.Changed -= OnSettingsFileChanged;
            _watcher.Created -= OnSettingsFileChanged;
            _watcher.Renamed -= OnSettingsFileRenamed;
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

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e) => QueueReload();

    private void OnSettingsFileRenamed(object sender, RenamedEventArgs e) => QueueReload();

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
                    && File.Exists(_settingsPath))
                {
                    await _settingsService.LoadAsync(request.Token).ConfigureAwait(false);
                    if (_settingsService.Current.ExternalSpellingEngineEnabled)
                    {
                        _symSpell?.WarmUp();
                    }
                    _logger.LogInformation("Resident configuration reloaded after settings window update.");
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
            // Save writes are short-lived. The next watcher event retries; no
            // user text is included in this diagnostic.
        }
        catch (UnauthorizedAccessException)
        {
            // Treat a concurrent editor lock as a transient unavailable file.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resident configuration reload failed safely.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingReload, null, request);
            request.Dispose();
        }
    }
}
