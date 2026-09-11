using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsPredictionEscInterceptor : IPredictionEscInterceptor
{
    private readonly IInputObservationFilter _observationFilter;
    private readonly IPredictionEscDismissalGate _dismissalGate;
    private readonly ITextReplacementSessionNotifier _replacementSessionNotifier;
    private readonly IBoundaryKeyPairingTracker _pairingTracker;

    public WindowsPredictionEscInterceptor(
        IInputObservationFilter observationFilter,
        IPredictionEscDismissalGate dismissalGate,
        ITextReplacementSessionNotifier replacementSessionNotifier,
        IBoundaryKeyPairingTracker pairingTracker)
    {
        _observationFilter = observationFilter;
        _dismissalGate = dismissalGate;
        _replacementSessionNotifier = replacementSessionNotifier;
        _pairingTracker = pairingTracker;
    }

    public PredictionEscInterceptResult TryIntercept(
        KeyboardObservationEventArgs observation,
        KeyboardHookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!_observationFilter.ShouldObserve(metadata))
        {
            return PredictionEscInterceptResult.PassThrough();
        }

        if (_replacementSessionNotifier.IsReplacementActive)
        {
            return PredictionEscInterceptResult.PassThrough();
        }

        if (observation.EventType == KeyEventType.KeyUp)
        {
            if (_pairingTracker.TrySuppressMatchingKeyUp(
                    observation.VirtualKeyCode,
                    observation.ScanCode))
            {
                return PredictionEscInterceptResult.SuppressEscDismissal();
            }

            return PredictionEscInterceptResult.PassThrough();
        }

        if (observation.EventType != KeyEventType.KeyDown)
        {
            return PredictionEscInterceptResult.PassThrough();
        }

        if (!IsPlainEscape(observation))
        {
            return PredictionEscInterceptResult.PassThrough();
        }

        if (!_dismissalGate.ShouldInterceptPlainEsc())
        {
            return PredictionEscInterceptResult.PassThrough();
        }

        _pairingTracker.RegisterSuppressedKeyDown(
            observation.VirtualKeyCode,
            observation.ScanCode);

        return PredictionEscInterceptResult.SuppressEscDismissal();
    }

    public static bool IsPlainEscape(
        KeyboardObservationEventArgs observation,
        Func<int, bool>? isKeyDownOverride = null)
    {
        if (observation.VirtualKeyCode != VirtualKeys.Escape)
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
