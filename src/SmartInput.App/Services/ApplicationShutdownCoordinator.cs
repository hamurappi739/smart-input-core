using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SmartInput.App.Services;

public interface IApplicationShutdownCoordinator
{
    bool CleanupPerformed { get; }

    Task PerformCleanupAsync();
}

public sealed class ApplicationShutdownCoordinator : IApplicationShutdownCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationShutdownCoordinator> _logger;
    private int _cleanupPerformed;

    public ApplicationShutdownCoordinator(
        IServiceProvider serviceProvider,
        ILogger<ApplicationShutdownCoordinator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public bool CleanupPerformed => Volatile.Read(ref _cleanupPerformed) == 1;

    public async Task PerformCleanupAsync()
    {
        if (Interlocked.CompareExchange(ref _cleanupPerformed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await DisposeAsyncCoordinator<IDoubleShiftUndoCoordinator>().ConfigureAwait(false);
            await DisposeAsyncCoordinator<IAutomaticLayoutHotkeyCoordinator>().ConfigureAwait(false);
            await DisposeAsyncCoordinator<ICorrectionNotificationCoordinator>().ConfigureAwait(false);
            await DisposeAsyncCoordinator<ISystemTrayCoordinator>().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application shutdown cleanup failed.");
        }
    }

    private async Task DisposeAsyncCoordinator<T>() where T : class
    {
        if (_serviceProvider.GetService<T>() is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}
