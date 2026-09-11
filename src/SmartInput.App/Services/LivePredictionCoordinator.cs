using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.App.Services;

public interface ILivePredictionCoordinator
{
    LivePredictionStatus Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void ResetBuffer();
}

public sealed class LivePredictionCoordinator : ILivePredictionCoordinator, IDisposable
{
    private readonly IInputMonitor _inputMonitor;
    private readonly IKeyboardCharacterResolver _characterResolver;
    private readonly ILivePredictionEngine _predictionEngine;
    private readonly IActiveApplicationService _activeApplicationService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly ILogger<LivePredictionCoordinator> _logger;
    private readonly object _handlerSync = new();

    private nint _lastWindowHandle;
    private string _lastProcessName = string.Empty;
    private bool _hasApplicationContext;
    private bool _lastEmergencyPaused;
    private bool _initialized;

    public LivePredictionCoordinator(
        IInputMonitor inputMonitor,
        IKeyboardCharacterResolver characterResolver,
        ILivePredictionEngine predictionEngine,
        IActiveApplicationService activeApplicationService,
        IAutomationSafetyService automationSafetyService,
        IEmergencyPauseService emergencyPauseService,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        ILogger<LivePredictionCoordinator> logger)
    {
        _inputMonitor = inputMonitor;
        _characterResolver = characterResolver;
        _predictionEngine = predictionEngine;
        _activeApplicationService = activeApplicationService;
        _automationSafetyService = automationSafetyService;
        _emergencyPauseService = emergencyPauseService;
        _replacementSessionNotifier = replacementSessionNotifier;
        _logger = logger;
    }

    public LivePredictionStatus Status => _predictionEngine.Status;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _inputMonitor.InputObserved += OnInputObserved;
        _initialized = true;
        _lastEmergencyPaused = _emergencyPauseService.IsPaused;

        var initialPolicy = await _automationSafetyService
            .EvaluateCurrentContextAsync(cancellationToken)
            .ConfigureAwait(false);
        _predictionEngine.NotifyPolicyContextChanged(initialPolicy);
    }

    public void ResetBuffer()
    {
        _predictionEngine.ResetBuffer("manual-reset");
    }

    public void Dispose()
    {
        lock (_handlerSync)
        {
            _inputMonitor.InputObserved -= OnInputObserved;
        }
    }

    private void OnInputObserved(object? sender, KeyboardObservationEventArgs observation)
    {
        _ = ProcessObservationAsync(observation);
    }

    private async Task ProcessObservationAsync(KeyboardObservationEventArgs observation)
    {
        try
        {
            if (_replacementSessionNotifier.IsReplacementActive)
            {
                return;
            }

            if (await DetectContextChangesAsync().ConfigureAwait(false))
            {
                return;
            }

            var resolution = _characterResolver.Resolve(observation);
            var tokenInput = MapResolution(observation, resolution);
            if (tokenInput is null)
            {
                return;
            }

            await _predictionEngine.ProcessInputAsync(tokenInput).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live prediction processing failed.");
            _predictionEngine.ResetBuffer("processing-error");
        }
    }

    private async Task<bool> DetectContextChangesAsync()
    {
        if (_emergencyPauseService.IsPaused != _lastEmergencyPaused)
        {
            _lastEmergencyPaused = _emergencyPauseService.IsPaused;
            _predictionEngine.NotifyPolicyContextChanged(AutomationPolicyResult.EmergencyPaused());
            return _lastEmergencyPaused;
        }

        var activeApplication = await _activeApplicationService
            .GetActiveApplicationAsync()
            .ConfigureAwait(false);

        var policy = await _automationSafetyService
            .EvaluateCurrentContextAsync()
            .ConfigureAwait(false);

        _predictionEngine.NotifyPolicyContextChanged(policy);

        if (activeApplication is null)
        {
            if (_hasApplicationContext)
            {
                _hasApplicationContext = false;
                _lastWindowHandle = 0;
                _lastProcessName = string.Empty;
                _predictionEngine.NotifyApplicationContextChanged();
            }

            return true;
        }

        if (!_hasApplicationContext)
        {
            _hasApplicationContext = true;
            _lastWindowHandle = activeApplication.WindowHandle;
            _lastProcessName = activeApplication.ProcessName;
        }
        else if (activeApplication.WindowHandle != _lastWindowHandle
            || !string.Equals(activeApplication.ProcessName, _lastProcessName, StringComparison.OrdinalIgnoreCase))
        {
            _lastWindowHandle = activeApplication.WindowHandle;
            _lastProcessName = activeApplication.ProcessName;
            _predictionEngine.NotifyApplicationContextChanged();
            return true;
        }

        return false;
    }

    private static TokenInputEvent? MapResolution(
        KeyboardObservationEventArgs observation,
        KeyboardCharacterResolution resolution)
    {
        return resolution.Kind switch
        {
            CharacterResolutionKind.Character => TokenInputEvent.CharacterInput(resolution.Character),
            CharacterResolutionKind.WordBoundary => TokenInputEvent.Boundary(observation.SuppressedBoundary),
            CharacterResolutionKind.Backspace => TokenInputEvent.Backspace,
            CharacterResolutionKind.Reset => TokenInputEvent.Reset,
            CharacterResolutionKind.Uncertain => TokenInputEvent.Uncertain,
            _ => null,
        };
    }
}
