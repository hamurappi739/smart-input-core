using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Services;

/// <summary>
/// Bounded buffer for explicit snippet triggers. Accepts printable non-whitespace
/// characters (including symbols such as '/' and '@'), separate from the letter-only
/// layout/autocorrection token buffer.
/// </summary>
public sealed class SnippetTriggerBuffer
{
    private readonly int _maxTriggerLength;
    private readonly StringBuilder _buffer = new();

    public SnippetTriggerBuffer(SnippetTriggerBufferOptions? options = null)
    {
        options ??= new SnippetTriggerBufferOptions();
        _maxTriggerLength = options.MaxTriggerLength;
    }

    public int Length => _buffer.Length;

    public bool IsEmpty => _buffer.Length == 0;

    public TokenBufferApplyResult Apply(TokenInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Kind switch
        {
            TokenInputKind.Reset => Clear(TokenBufferResetReason.ExplicitReset),
            TokenInputKind.Uncertain => Clear(TokenBufferResetReason.UncertainInput),
            TokenInputKind.Backspace => ApplyBackspace(),
            TokenInputKind.Character => ApplyCharacter(input.Character),
            TokenInputKind.WordBoundary => CompleteTrigger(),
            _ => Clear(TokenBufferResetReason.UncertainInput),
        };
    }

    internal string TakeCompletedTrigger()
    {
        var trigger = _buffer.ToString();
        _buffer.Clear();
        return trigger;
    }

    internal void ClearBuffer()
    {
        _buffer.Clear();
    }

    private TokenBufferApplyResult ApplyCharacter(char character)
    {
        if (!InputCharacterClassification.IsTrackableTriggerCharacter(character))
        {
            return Clear(TokenBufferResetReason.NonTokenCharacter);
        }

        if (_buffer.Length >= _maxTriggerLength)
        {
            return Clear(TokenBufferResetReason.MaxLengthExceeded);
        }

        _buffer.Append(character);
        return TokenBufferApplyResult.Changed();
    }

    private TokenBufferApplyResult ApplyBackspace()
    {
        if (_buffer.Length == 0)
        {
            return Clear(TokenBufferResetReason.UnsafeBackspace);
        }

        _buffer.Length--;
        return TokenBufferApplyResult.Changed();
    }

    private TokenBufferApplyResult CompleteTrigger()
    {
        if (_buffer.Length == 0)
        {
            return TokenBufferApplyResult.NoChange();
        }

        return TokenBufferApplyResult.ForCompletedToken(_buffer.Length);
    }

    private TokenBufferApplyResult Clear(TokenBufferResetReason reason)
    {
        var hadContent = _buffer.Length > 0;
        _buffer.Clear();
        return TokenBufferApplyResult.Reset(reason, hadContent);
    }
}
