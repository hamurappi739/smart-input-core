using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartInput.App.Services;
using SmartInput.Core.Services;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Prediction has its own native overlay and timers. It is intentionally not
/// resolved in the resident process unless the user has enabled prediction.
/// Layout, spelling, punctuation and Undo do not depend on it.
/// </summary>
internal sealed class ResidentPredictionFeatureCoordinator : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly ILogger<ResidentPredictionFeatureCoordinator> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);

    private ILivePredictionCoordinator? _prediction;
    private ILivePredictionOverlayCoordinator? _overlay;
    private ILivePredictionTabAcceptanceCoordinator? _tab;
    private ILivePredictionEscDismissalCoordinator? _escape;
    private bool _started;
    private bool _disposed;

    public ResidentPredictionFeatureCoordinator(
        IServiceProvider services,
        ISettingsService settings,
        ILogger<ResidentPredictionFeatureCoordinator> logger)
    {
        _services = services;
        _settings = settings;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _settings.SettingsChanged += OnSettingsChanged;
        await StartIfEnabledAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;

        if (_escape is IAsyncDisposable escape)
        {
            await escape.DisposeAsync().ConfigureAwait(false);
        }

        if (_tab is IAsyncDisposable tab)
        {
            await tab.DisposeAsync().ConfigureAwait(false);
        }

        if (_overlay is IAsyncDisposable overlay)
        {
            await overlay.DisposeAsync().ConfigureAwait(false);
        }

        if (_prediction is IDisposable prediction)
        {
            prediction.Dispose();
        }

        _startGate.Dispose();
    }

    private void OnSettingsChanged()
    {
        if (_settings.Current.PredictionEnabled)
        {
            _ = StartIfEnabledAsync(CancellationToken.None);
        }
    }

    private async Task StartIfEnabledAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_settings.Current.PredictionEnabled || _started)
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || !_settings.Current.PredictionEnabled || _started)
            {
                return;
            }

            // Resolve only at this point: this keeps the overlay, associated
            // native handles and timer state out of a prediction-disabled idle
            // process. Once enabled it remains ready until host shutdown.
            _prediction = _services.GetRequiredService<ILivePredictionCoordinator>();
            _overlay = _services.GetRequiredService<ILivePredictionOverlayCoordinator>();
            _tab = _services.GetRequiredService<ILivePredictionTabAcceptanceCoordinator>();
            _escape = _services.GetRequiredService<ILivePredictionEscDismissalCoordinator>();

            await _prediction.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _overlay.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _tab.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _escape.InitializeAsync(cancellationToken).ConfigureAwait(false);

            _started = true;
            _logger.LogInformation("Resident prediction components initialized after opt-in.");
        }
        finally
        {
            _startGate.Release();
        }
    }
}
