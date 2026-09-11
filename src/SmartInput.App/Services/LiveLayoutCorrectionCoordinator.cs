using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.App.Services;

public interface ILiveLayoutCorrectionCoordinator
{
    LiveLayoutCorrectionStatus Status { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void ResetBuffer();
}

public sealed class LiveLayoutCorrectionCoordinator : ILiveLayoutCorrectionCoordinator, IDisposable
{
    private readonly IInputMonitor _inputMonitor;
    private readonly IKeyboardCharacterResolver _characterResolver;
    private readonly IAutomaticLayoutCorrectionEngine _correctionEngine;
    private readonly IActiveApplicationService _activeApplicationService;
    private readonly IAutomationSafetyService _automationSafetyService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly IBoundaryKeyPairingTracker _boundaryKeyPairingTracker;
    private readonly IBoundaryKeyDeliveryService _boundaryKeyDeliveryService;
    private readonly ILiveLayoutBoundaryGate _boundaryGate;
    private readonly ICorrectionUndoService _correctionUndoService;
    private readonly ICorrectionRejectionBackspaceService _backspaceRejectionService;
    private readonly ICorrectionApplicationContext _correctionApplicationContext;
    private readonly ICorrectionFeedbackNotifier _feedbackNotifier;
    private readonly IUserAutocorrectDictionaryStore _userDictionaryStore;
    private readonly ICorrectionRejectionLearningStore _rejectionLearningStore;
    private readonly RecentTextContextBuffer? _recentTextContext;
    private readonly ILogger<LiveLayoutCorrectionCoordinator> _logger;
    private readonly IPerformanceMetricsRecorder? _performanceMetrics;
    private readonly object _handlerSync = new();
    private readonly SerializedObservationQueue _observationQueue;
    private readonly SemaphoreSlim _processingGate = new(1, 1);

    private nint _lastWindowHandle;
    private string _lastProcessName = string.Empty;
    private bool _hasApplicationContext;
    private bool _lastEmergencyPaused;
    private bool _initialized;

    public LiveLayoutCorrectionCoordinator(
        IInputMonitor inputMonitor,
        IKeyboardCharacterResolver characterResolver,
        IAutomaticLayoutCorrectionEngine correctionEngine,
        IActiveApplicationService activeApplicationService,
        IAutomationSafetyService automationSafetyService,
        IEmergencyPauseService emergencyPauseService,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        IBoundaryKeyPairingTracker boundaryKeyPairingTracker,
        IBoundaryKeyDeliveryService boundaryKeyDeliveryService,
        ILiveLayoutBoundaryGate boundaryGate,
        ICorrectionUndoService correctionUndoService,
        ICorrectionRejectionBackspaceService backspaceRejectionService,
        ICorrectionApplicationContext correctionApplicationContext,
        ICorrectionFeedbackNotifier feedbackNotifier,
        IUserAutocorrectDictionaryStore userDictionaryStore,
        ICorrectionRejectionLearningStore rejectionLearningStore,
        ILogger<LiveLayoutCorrectionCoordinator> logger,
        IPerformanceMetricsRecorder? performanceMetrics = null,
        RecentTextContextBuffer? recentTextContext = null,
        SerializedObservationQueue? observationQueue = null)
    {
        _inputMonitor = inputMonitor;
        _characterResolver = characterResolver;
        _correctionEngine = correctionEngine;
        _activeApplicationService = activeApplicationService;
        _automationSafetyService = automationSafetyService;
        _emergencyPauseService = emergencyPauseService;
        _replacementSessionNotifier = replacementSessionNotifier;
        _boundaryKeyPairingTracker = boundaryKeyPairingTracker;
        _boundaryKeyDeliveryService = boundaryKeyDeliveryService;
        _boundaryGate = boundaryGate;
        _correctionUndoService = correctionUndoService;
        _backspaceRejectionService = backspaceRejectionService;
        _correctionApplicationContext = correctionApplicationContext;
        _feedbackNotifier = feedbackNotifier;
        _userDictionaryStore = userDictionaryStore;
        _rejectionLearningStore = rejectionLearningStore;
        _logger = logger;
        _performanceMetrics = performanceMetrics;
        _recentTextContext = recentTextContext;
        _observationQueue = observationQueue ?? new SerializedObservationQueue();
    }

    public LiveLayoutCorrectionStatus Status => _correctionEngine.Status;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _userDictionaryStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _rejectionLearningStore.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        _inputMonitor.InputObserved += OnInputObserved;
        _initialized = true;
        _lastEmergencyPaused = _emergencyPauseService.IsPaused;

        var initialPolicy = await _automationSafetyService
            .EvaluateCurrentContextAsync(cancellationToken)
            .ConfigureAwait(false);
        _correctionEngine.NotifyPolicyContextChanged(initialPolicy);
        _performanceMetrics?.RecordLivePipelineStage(LivePipelineStage.PolicyEvaluated);
    }

    public void ResetBuffer()
    {
        // Dictionary/settings watchers can call this while observations are
        // already queued. Waiting only on _processingGate is insufficient:
        // an older queued character could run immediately after the reset
        // and repopulate the token with stale data. Put the reset in the same
        // FIFO as input observations so it is a real ordering point.
        _observationQueue
            .Enqueue(() =>
            {
                ResetBufferCore("manual-reset");
                return Task.CompletedTask;
            })
            .GetAwaiter()
            .GetResult();
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
        // Ordinary KeyUp events carry no text for this coordinator. They are
        // still delivered to the dedicated Double Shift/hotkey subscribers,
        // but skipping them here avoids a foreground-window lookup and secure
        // input policy evaluation after every physical key release.
        if (!ShouldProcessForTextCorrection(observation))
        {
            return;
        }

        // Every observation must enter one FIFO.  Fire-and-forget ordinary
        // characters used to let a boundary (or Double Shift) overtake the
        // character tasks that were still waiting for _processingGate.  That
        // made a prepared correction see an empty/short token intermittently
        // and left a leading character behind after manual layout toggle.
        // The queue is asynchronous, so the hook thread still never waits;
        // only a physically suppressed boundary waits for its own queued work
        // before the monitor can release/replay the boundary.
        var processing = _observationQueue.Enqueue(
            () => ProcessObservationAsync(observation));

        if (observation.SuppressedBoundary is not null)
        {
            processing.GetAwaiter().GetResult();
        }
    }

    internal static bool ShouldProcessForTextCorrection(
        KeyboardObservationEventArgs observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return observation.EventType == SmartInput.Platform.Abstractions.Input.KeyEventType.KeyDown
            || observation.SuppressedBoundary is not null
            || observation.PreparedLayoutCorrection is not null
            || observation.IsPredictionTabAcceptance
            || observation.IsPredictionEscDismissal;
    }

    private async Task ProcessObservationAsync(KeyboardObservationEventArgs observation)
    {
        if (observation.IsEarlyLayoutCorrection)
        {
            // WH_KEYBOARD_LL runs before the physical key is placed in the
            // foreground application's input queue. Let the hook callback
            // return before SendInput posts the Backspace/Unicode batch;
            // otherwise a very fast coordinator can delete only three of the
            // four prefix characters. One millisecond is an ordering yield,
            // not the old user-visible 250 ms barrier.
            await Task.Delay(1).ConfigureAwait(false);
        }

        var deliveredBoundariesBefore = observation.SuppressedBoundary is null
            ? -1
            : _correctionEngine.Status.BoundariesDelivered;

        await _processingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_replacementSessionNotifier.IsReplacementActive)
            {
                await DeliverSuppressedBoundaryFailOpenAsync(observation).ConfigureAwait(false);
                return;
            }

            var contextChange = await DetectContextChangesAsync(
                    observation.ForegroundWindowHandle)
                .ConfigureAwait(false);
            if (contextChange.Changed && observation.SuppressedBoundary is not null)
            {
                // The hook may have suppressed a boundary just before the
                // foreground/policy snapshot changed.  Never drop that
                // physical key: the correction is no longer safe, so deliver
                // the original boundary exactly once and fail open.
                await DeliverSuppressedBoundaryFailOpenAsync(observation).ConfigureAwait(false);
                return;
            }

            if (!contextChange.EventBelongsToCurrentContext)
            {
                // The event was queued for the previous foreground window,
                // but the target has already changed. It was already allowed
                // through to that previous target; feeding it into the new
                // token buffer would manufacture a leading character.
                return;
            }

            var resolution = ResolveObservation(observation);
            if (contextChange.Changed && !contextChange.GateWasSynchronizedOnHook)
            {
                // The low-level hook updates the boundary preflight before
                // this queued coordinator callback observes the foreground
                // application.  A focus change therefore resets the gate
                // after it has already seen the current key.  Re-apply only
                // that hook-resolved character so the preflight and Core
                // buffers start the new context from the same input exactly
                // once.  Boundaries are deliberately excluded: a boundary
                // that crossed a context change already failed open above.
                RebaseBoundaryGateAfterContextChange(resolution);
            }

            var tokenInput = MapResolution(observation, resolution);
            if (tokenInput is null)
            {
                return;
            }

            // A pass-through boundary has already reached the target, so it
            // must not be handed to the correction engine as a replacement
            // boundary. Still feed its hook-time character into the rolling
            // context. Otherwise a fast/deferred sequence can lose the space
            // internally and the next Double Shift may operate on stale text.
            if (tokenInput.Kind == TokenInputKind.WordBoundary
                && tokenInput.DeferredBoundary is null
                && resolution.Boundary is not null)
            {
                _recentTextContext?.Apply(TokenInputEvent.Boundary(resolution.Boundary));
            }

            if (tokenInput.Kind == TokenInputKind.WordBoundary
                && !string.IsNullOrEmpty(tokenInput.CompletedToken))
            {
                // The hook-thread gate has a complete token snapshot. Keep it
                // as the explicit Double Shift source and as a fallback for
                // the async Core token buffer if the queue crossed a context
                // transition.
                _recentTextContext?.RememberCompletedToken(tokenInput.CompletedToken);
            }

            await _backspaceRejectionService.ProcessInputAsync(tokenInput).ConfigureAwait(false);
            NotifyGenuineUserInputIfNeeded(tokenInput);

            await _correctionEngine.ProcessInputAsync(tokenInput).ConfigureAwait(false);
            _performanceMetrics?.RecordLivePipelineStage(LivePipelineStage.DecisionEvaluated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live layout correction processing failed.");
            ResetBufferCore("processing-error");

            // A boundary is physically suppressed before this async handler
            // runs. If an exception happened before the engine delivered it,
            // fail open and deliver that exact key once. The counter check
            // avoids replaying a boundary that the engine already delivered
            // from its own finally block.
            if (observation.SuppressedBoundary is not null
                && _correctionEngine.Status.BoundariesDelivered <= deliveredBoundariesBefore)
            {
                await DeliverSuppressedBoundaryFailOpenAsync(observation).ConfigureAwait(false);
            }
        }
        finally
        {
            _processingGate.Release();
        }
    }

    private async Task DeliverSuppressedBoundaryFailOpenAsync(
        KeyboardObservationEventArgs observation)
    {
        if (observation.SuppressedBoundary is null)
        {
            return;
        }

        try
        {
            await _boundaryKeyDeliveryService
                .DeliverAsync(observation.SuppressedBoundary)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deliver a deferred boundary while failing open.");
        }
    }

    private async Task<ContextChangeResult> DetectContextChangesAsync(
        nint hookWindowHandle)
    {
        if (_emergencyPauseService.IsPaused != _lastEmergencyPaused)
        {
            _lastEmergencyPaused = _emergencyPauseService.IsPaused;
            _boundaryKeyPairingTracker.ClearPending(BoundaryPairingCleanupReason.EmergencyPause);
            ResetBufferCore("emergency-pause-changed");
            _correctionUndoService.NotifyPolicyContextChanged(AutomationPolicyResult.EmergencyPaused());
            _backspaceRejectionService.NotifyPolicyContextChanged(AutomationPolicyResult.EmergencyPaused());
            _feedbackNotifier.NotifyContextInvalidated();
            return new ContextChangeResult(
                Changed: _lastEmergencyPaused,
                EventBelongsToCurrentContext: true,
                GateWasSynchronizedOnHook: false);
        }

        var activeApplication = await _activeApplicationService
            .GetActiveApplicationAsync()
            .ConfigureAwait(false);

        var policy = await _automationSafetyService
            .EvaluateCurrentContextAsync(activeApplication)
            .ConfigureAwait(false);

        _correctionEngine.NotifyPolicyContextChanged(policy);
        _performanceMetrics?.RecordLivePipelineStage(LivePipelineStage.PolicyEvaluated);
        _correctionUndoService.NotifyPolicyContextChanged(policy);
        _backspaceRejectionService.NotifyPolicyContextChanged(policy);

        if (activeApplication is null)
        {
            if (_hasApplicationContext)
            {
                _hasApplicationContext = false;
                _lastWindowHandle = 0;
                _lastProcessName = string.Empty;
                _correctionApplicationContext.Update(null, 0);
                _boundaryKeyPairingTracker.ClearPending(BoundaryPairingCleanupReason.FocusChange);
                _correctionEngine.NotifyApplicationContextChanged();
                _boundaryGate.ResetPendingToken();
                _correctionUndoService.NotifyApplicationContextChanged(null, 0);
                _backspaceRejectionService.NotifyApplicationContextChanged(null, 0);
                _feedbackNotifier.NotifyContextInvalidated();
            }

            return new ContextChangeResult(
                Changed: true,
                EventBelongsToCurrentContext: false,
                GateWasSynchronizedOnHook: false);
        }

        _correctionApplicationContext.Update(activeApplication.ProcessName, activeApplication.WindowHandle);

        if (!_hasApplicationContext)
        {
            _hasApplicationContext = true;
            _lastWindowHandle = activeApplication.WindowHandle;
            _lastProcessName = activeApplication.ProcessName;

            // The first event for a valid foreground application is safe to
            // process. Returning true here used to discard the first
            // character after host startup/focus acquisition, leaving the
            // internal token one character short (for example `пзе` became
            // `зе`, so a manual toggle left `пgpt`).
            return new ContextChangeResult(
                Changed: false,
                EventBelongsToCurrentContext: hookWindowHandle == 0
                    || hookWindowHandle == activeApplication.WindowHandle,
                GateWasSynchronizedOnHook: hookWindowHandle != 0
                    && hookWindowHandle == activeApplication.WindowHandle);
        }
        else if (activeApplication.WindowHandle != _lastWindowHandle
            || !string.Equals(activeApplication.ProcessName, _lastProcessName, StringComparison.OrdinalIgnoreCase))
        {
            _lastWindowHandle = activeApplication.WindowHandle;
            _lastProcessName = activeApplication.ProcessName;
            _boundaryKeyPairingTracker.ClearPending(BoundaryPairingCleanupReason.FocusChange);
            _correctionEngine.NotifyApplicationContextChanged();
            var gateWasSynchronizedOnHook = hookWindowHandle != 0
                && hookWindowHandle == activeApplication.WindowHandle;
            if (!gateWasSynchronizedOnHook)
            {
                // Test/fallback adapters may not provide a hook-time window
                // identity. Keep the old fail-open rebase path for them.
                _boundaryGate.ResetPendingToken();
            }
            _correctionUndoService.NotifyApplicationContextChanged(
                activeApplication.ProcessName,
                activeApplication.WindowHandle);
            _backspaceRejectionService.NotifyApplicationContextChanged(
                activeApplication.ProcessName,
                activeApplication.WindowHandle);
            _feedbackNotifier.NotifyContextInvalidated();
            return new ContextChangeResult(
                Changed: true,
                EventBelongsToCurrentContext: hookWindowHandle == 0
                    || hookWindowHandle == activeApplication.WindowHandle,
                GateWasSynchronizedOnHook: gateWasSynchronizedOnHook);
        }

        return new ContextChangeResult(
            Changed: false,
            EventBelongsToCurrentContext: hookWindowHandle == 0
                || hookWindowHandle == activeApplication.WindowHandle,
            GateWasSynchronizedOnHook: hookWindowHandle != 0
                && hookWindowHandle == activeApplication.WindowHandle);
    }

    private readonly record struct ContextChangeResult(
        bool Changed,
        bool EventBelongsToCurrentContext,
        bool GateWasSynchronizedOnHook);

    private void NotifyGenuineUserInputIfNeeded(TokenInputEvent tokenInput)
    {
        if (tokenInput.Kind is TokenInputKind.Character
            or TokenInputKind.Backspace
            or TokenInputKind.Reset
            or TokenInputKind.Uncertain)
        {
            _correctionUndoService.NotifyGenuineUserInput();
            _feedbackNotifier.NotifyUserInput();
        }
    }

    private void RebaseBoundaryGateAfterContextChange(KeyboardCharacterResolution resolution)
    {
        if (resolution.Kind == CharacterResolutionKind.Character)
        {
            _ = _boundaryGate.ObserveKeyDown(resolution);
        }
    }

    private void ResetBufferCore(string reason)
    {
        _correctionEngine.ResetBuffer(reason);
        _boundaryGate.ResetPendingToken();
    }

    private static TokenInputEvent? MapResolution(
            KeyboardObservationEventArgs observation,
        KeyboardCharacterResolution resolution)
    {
        return resolution.Kind switch
        {
            CharacterResolutionKind.Character => TokenInputEvent.CharacterInput(
                resolution.Character,
                observation.PreparedLayoutCorrection,
                observation.IsEarlyLayoutCorrection),
            CharacterResolutionKind.WordBoundary => TokenInputEvent.Boundary(
                observation.SuppressedBoundary,
                observation.PreparedLayoutCorrection,
                observation.CompletedToken),
            CharacterResolutionKind.Backspace => TokenInputEvent.Backspace,
            CharacterResolutionKind.Reset => TokenInputEvent.Reset,
            CharacterResolutionKind.Uncertain => TokenInputEvent.Uncertain,
            _ => null,
        };
    }

    private KeyboardCharacterResolution ResolveObservation(KeyboardObservationEventArgs observation)
    {
        if (observation.Resolved is not null)
        {
            return observation.Resolved;
        }

        if (observation.ResolvedBoundary is not null)
        {
            return KeyboardCharacterResolution.CreateBoundary(observation.ResolvedBoundary);
        }

        if (observation.ResolvedCharacter is char character)
        {
            return KeyboardCharacterResolution.CharacterOf(character);
        }

        return _characterResolver.Resolve(observation);
    }
}
