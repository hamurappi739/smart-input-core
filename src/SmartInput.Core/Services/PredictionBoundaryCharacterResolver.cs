using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Services;

internal static class PredictionBoundaryCharacterResolver
{
    internal static bool IsNavigationBoundary(DeferredBoundaryKey? boundary)
    {
        if (boundary is null)
        {
            return false;
        }

        return boundary.VirtualKeyCode is VirtualKeys.Tab or VirtualKeys.Return;
    }

    internal static char? TryGetAppendableBoundaryCharacter(DeferredBoundaryKey? boundary)
    {
        if (boundary is null)
        {
            return ' ';
        }

        if (boundary.DeliveryKind == BoundaryDeliveryKind.UnicodeCharacter
            && boundary.Character is char character)
        {
            return InputCharacterClassification.IsWordBoundaryCharacter(character)
                ? character
                : null;
        }

        return boundary.VirtualKeyCode switch
        {
            VirtualKeys.Space => ' ',
            _ => null,
        };
    }
}
