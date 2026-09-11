using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.App.Services;

public sealed class InputDiagnosticCoordinator : IInputDiagnosticCoordinator, IDisposable
{
    private readonly IInputMonitor _inputMonitor;
    private readonly ISettingsService _settingsService;
    private readonly InputDiagnosticBuffer _buffer;
    private readonly ILogger<InputDiagnosticCoordinator> _logger;
    private readonly object _handlerSync = new();
    private bool _initialized;

    public InputDiagnosticCoordinator(
        IInputMonitor inputMonitor,
        ISettingsService settingsService,
        ILogger<InputDiagnosticCoordinator> logger)
    {
        _inputMonitor = inputMonitor;
        _settingsService = settingsService;
        _logger = logger;
        _buffer = new InputDiagnosticBuffer();
    }

    public bool IsMonitoring => _inputMonitor.IsMonitoring;

    public event EventHandler<InputDiagnosticEvent>? DiagnosticReceived;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

        _inputMonitor.InputObserved += OnInputObserved;
        _initialized = true;

        await ApplyProtectionStateAsync(_settingsService.Current.IsEnabled, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyProtectionStateAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            return;
        }

        if (enabled)
        {
            await _inputMonitor.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Input diagnostic monitoring enabled.");
            return;
        }

        await _inputMonitor.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Input diagnostic monitoring disabled.");
    }

    public IReadOnlyList<InputDiagnosticEvent> GetRecentEvents()
    {
        return _buffer.Snapshot();
    }

    public void ClearRecentEvents()
    {
        _buffer.Clear();
    }

    public void Dispose()
    {
        lock (_handlerSync)
        {
            _inputMonitor.InputObserved -= OnInputObserved;
        }

        if (_inputMonitor is IDisposable disposableMonitor)
        {
            disposableMonitor.Dispose();
        }
    }

    private void OnInputObserved(object? sender, KeyboardObservationEventArgs observation)
    {
        _ = PublishDiagnosticAsync(observation);
    }

    private Task PublishDiagnosticAsync(KeyboardObservationEventArgs observation)
    {
        try
        {
            var diagnostic = InputDiagnosticEventFactory.Create(
                MapEventType(observation.EventType),
                observation.VirtualKeyCode,
                processName: string.Empty,
                windowTitle: string.Empty,
                windowClassName: string.Empty,
                _inputMonitor.IsMonitoring,
                observation.TimestampUtc);

            _buffer.Add(diagnostic);

            EventHandler<InputDiagnosticEvent>? handler;
            lock (_handlerSync)
            {
                handler = DiagnosticReceived;
            }

            handler?.Invoke(this, diagnostic);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish input diagnostic event.");
            return Task.CompletedTask;
        }
    }

    private static Core.Models.KeyEventType MapEventType(PlatformKeyEventType eventType)
    {
        return eventType switch
        {
            PlatformKeyEventType.KeyDown => Core.Models.KeyEventType.KeyDown,
            PlatformKeyEventType.KeyUp => Core.Models.KeyEventType.KeyUp,
            _ => Core.Models.KeyEventType.KeyDown,
        };
    }
}
