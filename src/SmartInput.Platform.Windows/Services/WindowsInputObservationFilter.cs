using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsInputObservationFilter : IInputObservationFilter
{
    public bool ShouldObserve(KeyboardHookMetadata metadata)
    {
        return InputObservationRules.ShouldObserveUserInput(
            metadata,
            SmartInputInjectionMarkers.SmartInputExtraInfo);
    }
}
