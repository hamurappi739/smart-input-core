namespace SmartInput.Platform.Abstractions.Input;

public static class InputObservationRules
{
    public static bool IsInjectedInput(KeyboardHookMetadata metadata, nuint smartInputMarker)
    {
        if ((metadata.Flags & KeyboardHookFlags.Injected) != 0)
        {
            return true;
        }

        if (metadata.ExtraInfo == smartInputMarker)
        {
            return true;
        }

        return false;
    }

    public static bool ShouldObserveUserInput(KeyboardHookMetadata metadata, nuint smartInputMarker)
    {
        return !IsInjectedInput(metadata, smartInputMarker);
    }

    public static bool ShouldAbortReplacementOnUserInput(KeyEventType eventType, bool isInjectedInput)
    {
        if (isInjectedInput)
        {
            return false;
        }

        return eventType == KeyEventType.KeyDown;
    }
}
