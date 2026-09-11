namespace SmartInput.Platform.Abstractions.Input;

public static class KeyboardHookFlags
{
    public const uint Injected = 0x10;
}

public readonly record struct KeyboardHookMetadata(uint Flags, nuint ExtraInfo);

public interface IInputObservationFilter
{
    bool ShouldObserve(KeyboardHookMetadata metadata);
}
