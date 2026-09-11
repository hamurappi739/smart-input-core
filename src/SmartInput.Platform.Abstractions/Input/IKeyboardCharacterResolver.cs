namespace SmartInput.Platform.Abstractions.Input;

public enum CharacterResolutionKind
{
    Ignored,
    Character,
    WordBoundary,
    Backspace,
    Reset,
    Uncertain,
}

public sealed class KeyboardCharacterResolution
{
    public CharacterResolutionKind Kind { get; init; }

    public char Character { get; init; }

    public DeferredBoundaryKey? Boundary { get; init; }

    public static KeyboardCharacterResolution Ignored()
    {
        return new KeyboardCharacterResolution { Kind = CharacterResolutionKind.Ignored };
    }

    public static KeyboardCharacterResolution Reset()
    {
        return new KeyboardCharacterResolution { Kind = CharacterResolutionKind.Reset };
    }

    public static KeyboardCharacterResolution Uncertain()
    {
        return new KeyboardCharacterResolution { Kind = CharacterResolutionKind.Uncertain };
    }

    public static KeyboardCharacterResolution Backspace()
    {
        return new KeyboardCharacterResolution { Kind = CharacterResolutionKind.Backspace };
    }

    public static KeyboardCharacterResolution CreateBoundary(DeferredBoundaryKey boundary)
    {
        return new KeyboardCharacterResolution
        {
            Kind = CharacterResolutionKind.WordBoundary,
            Boundary = boundary,
        };
    }

    public static KeyboardCharacterResolution CharacterOf(char character)
    {
        return new KeyboardCharacterResolution
        {
            Kind = CharacterResolutionKind.Character,
            Character = character,
        };
    }
}

public interface IKeyboardCharacterResolver
{
    KeyboardCharacterResolution Resolve(KeyboardObservationEventArgs observation);
}
