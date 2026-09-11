using SmartInput.Platform.Abstractions.Diagnostics;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsBoundaryKeyInterceptor : IBoundaryKeyInterceptor
{
    private readonly IInputObservationFilter _observationFilter;
    private readonly IKeyboardCharacterResolver _characterResolver;
    private readonly ILiveLayoutBoundaryGate _boundaryGate;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly IBoundaryKeyPairingTracker _pairingTracker;
    private readonly IPerformanceMetricsRecorder? _performanceMetrics;

    public WindowsBoundaryKeyInterceptor(
        IInputObservationFilter observationFilter,
        IKeyboardCharacterResolver characterResolver,
        ILiveLayoutBoundaryGate boundaryGate,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        IBoundaryKeyPairingTracker pairingTracker,
        IPerformanceMetricsRecorder? performanceMetrics = null)
    {
        _observationFilter = observationFilter;
        _characterResolver = characterResolver;
        _boundaryGate = boundaryGate;
        _replacementSessionNotifier = replacementSessionNotifier;
        _pairingTracker = pairingTracker;
        _performanceMetrics = performanceMetrics;
    }

    public BoundaryInterceptResult TryIntercept(
        KeyboardObservationEventArgs observation,
        KeyboardHookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!_observationFilter.ShouldObserve(metadata))
        {
            return BoundaryInterceptResult.PassThrough();
        }

        if (observation.EventType == KeyEventType.KeyUp)
        {
            if (_pairingTracker.TrySuppressMatchingKeyUp(
                    observation.VirtualKeyCode,
                    observation.ScanCode))
            {
                return BoundaryInterceptResult.SuppressPhysicalKeyUp();
            }

            return BoundaryInterceptResult.PassThrough();
        }

        if (observation.EventType != KeyEventType.KeyDown)
        {
            return BoundaryInterceptResult.PassThrough();
        }

        // The gate must see the foreground transition before it appends this
        // character. The coordinator is intentionally asynchronous and may
        // otherwise reset the gate after later characters are already in its
        // hook-thread preflight buffer.
        _boundaryGate.ObserveApplicationContext(observation.ForegroundWindowHandle);
        var resolution = _characterResolver.Resolve(observation);
        if (resolution.Kind != CharacterResolutionKind.Ignored)
        {
            _performanceMetrics?.RecordLivePipelineStage(LivePipelineStage.CharacterResolved);
        }
        var preparedCorrection = _boundaryGate.ObserveKeyDown(resolution);
        if (resolution.Kind != CharacterResolutionKind.WordBoundary)
        {
            return BoundaryInterceptResult.PassThrough(
                resolution: resolution,
                preparedCorrection: preparedCorrection,
                isEarlyLayoutCorrection: preparedCorrection is not null);
        }

        var completedToken = _boundaryGate.TakeCompletedToken();

        if (_replacementSessionNotifier.IsReplacementActive)
        {
            return BoundaryInterceptResult.PassThrough(completedToken, resolution);
        }

        if (preparedCorrection is null && !_boundaryGate.ShouldSuppressPendingBoundary())
        {
            return BoundaryInterceptResult.PassThrough(completedToken, resolution);
        }

        var boundary = resolution.Boundary
            ?? CreateFallbackBoundary(observation);

        _pairingTracker.RegisterSuppressedKeyDown(
            observation.VirtualKeyCode,
            observation.ScanCode);
        _performanceMetrics?.RecordLivePipelineStage(LivePipelineStage.BoundaryIntercepted);

        return BoundaryInterceptResult.Suppress(
            boundary,
            preparedCorrection,
            completedToken,
            resolution);
    }

    public bool HasPendingEarlyLayoutCorrection()
        => _boundaryGate.HasPendingEarlyLayoutCorrection();

    private static DeferredBoundaryKey CreateFallbackBoundary(KeyboardObservationEventArgs observation)
    {
        return DeferredBoundaryKey.FromVirtualKey(
            observation.VirtualKeyCode,
            observation.ScanCode);
    }
}
