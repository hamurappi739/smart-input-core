using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsPredictionTabInterceptor : IPredictionTabInterceptor
{
    private readonly IInputObservationFilter _observationFilter;
    private readonly IPredictionTabAcceptanceGate _acceptanceGate;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly IBoundaryKeyPairingTracker _pairingTracker;

    public WindowsPredictionTabInterceptor(
        IInputObservationFilter observationFilter,
        IPredictionTabAcceptanceGate acceptanceGate,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        IBoundaryKeyPairingTracker pairingTracker)
    {
        _observationFilter = observationFilter;
        _acceptanceGate = acceptanceGate;
        _replacementSessionNotifier = replacementSessionNotifier;
        _pairingTracker = pairingTracker;
    }

    public PredictionTabInterceptResult TryIntercept(
        KeyboardObservationEventArgs observation,
        KeyboardHookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!_observationFilter.ShouldObserve(metadata))
        {
            return PredictionTabInterceptResult.PassThrough();
        }

        if (_replacementSessionNotifier.IsReplacementActive)
        {
            return PredictionTabInterceptResult.PassThrough();
        }

        if (observation.EventType == KeyEventType.KeyUp)
        {
            if (_pairingTracker.TrySuppressMatchingKeyUp(
                    observation.VirtualKeyCode,
                    observation.ScanCode))
            {
                return PredictionTabInterceptResult.SuppressTabAcceptance();
            }

            return PredictionTabInterceptResult.PassThrough();
        }

        if (observation.EventType != KeyEventType.KeyDown)
        {
            return PredictionTabInterceptResult.PassThrough();
        }

        if (!IsPlainTab(observation))
        {
            return PredictionTabInterceptResult.PassThrough();
        }

        if (!_acceptanceGate.ShouldInterceptPlainTab())
        {
            return PredictionTabInterceptResult.PassThrough();
        }

        _pairingTracker.RegisterSuppressedKeyDown(
            observation.VirtualKeyCode,
            observation.ScanCode);

        return PredictionTabInterceptResult.SuppressTabAcceptance();
    }

    public static bool IsPlainTab(
        KeyboardObservationEventArgs observation,
        Func<int, bool>? isKeyDownOverride = null)
    {
        if (observation.VirtualKeyCode != VirtualKeys.Tab)
        {
            return false;
        }

        bool IsDown(int virtualKey) => isKeyDownOverride?.Invoke(virtualKey) ?? IsKeyDown(virtualKey);

        if (IsDown(VirtualKeys.Shift)
            || IsDown(VirtualKeys.Control)
            || IsDown(VirtualKeys.Menu)
            || IsDown(VirtualKeys.LWin)
            || IsDown(VirtualKeys.RWin))
        {
            return false;
        }

        return true;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        var state = Win32Keyboard.GetAsyncKeyState(virtualKey);
        return (state & 0x8000) != 0;
    }
}
