namespace SmartInput.Platform.Abstractions.Input;

/// <summary>
/// Classifies typed characters for the live word-boundary and snippet-trigger pipelines.
/// </summary>
public static class InputCharacterClassification
{
    public static bool IsExplicitBoundaryVirtualKey(int virtualKey)
    {
        return virtualKey is VirtualKeys.Space or VirtualKeys.Tab or VirtualKeys.Return;
    }

    public static bool IsWordBoundaryCharacter(char character)
    {
        if (char.IsWhiteSpace(character))
        {
            return true;
        }

        return character is '.' or ',' or ';' or ':' or '!' or '?'
            or ')' or ']' or '}' or '>' or '"' or '\'' or '»' or '…';
    }

    public static bool IsTrackableTriggerCharacter(char character)
    {
        if (char.IsControl(character) || char.IsWhiteSpace(character))
        {
            return false;
        }

        if (IsWordBoundaryCharacter(character))
        {
            return false;
        }

        return char.IsLetterOrDigit(character)
            || char.IsPunctuation(character)
            || char.IsSymbol(character)
            || character is '_' or '-' or '/' or '@' or '#' or '~' or '`'
                or '\\' or '|' or '*' or '+' or '=' or '[' or '{' or '<'
                or '&' or '%' or '$' or '^';
    }
}
