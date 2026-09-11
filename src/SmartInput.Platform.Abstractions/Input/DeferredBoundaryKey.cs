namespace SmartInput.Platform.Abstractions.Input;

public enum BoundaryDeliveryKind
{
    VirtualKey,
    UnicodeCharacter,
}

public sealed class DeferredBoundaryKey
{
    public int VirtualKeyCode { get; init; }

    public int ScanCode { get; init; }

    public char? Character { get; init; }

    public BoundaryDeliveryKind DeliveryKind { get; init; }

    public static DeferredBoundaryKey FromVirtualKey(int virtualKeyCode, int scanCode)
    {
        return new DeferredBoundaryKey
        {
            VirtualKeyCode = virtualKeyCode,
            ScanCode = scanCode,
            DeliveryKind = BoundaryDeliveryKind.VirtualKey,
        };
    }

    public static DeferredBoundaryKey FromUnicodeCharacter(int virtualKeyCode, int scanCode, char character)
    {
        return new DeferredBoundaryKey
        {
            VirtualKeyCode = virtualKeyCode,
            ScanCode = scanCode,
            Character = character,
            DeliveryKind = BoundaryDeliveryKind.UnicodeCharacter,
        };
    }
}
