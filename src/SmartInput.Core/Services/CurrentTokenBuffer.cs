using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public sealed class CurrentTokenBuffer
{
    private readonly int _maxTokenLength;
    private readonly StringBuilder _buffer = new();

    public CurrentTokenBuffer(CurrentTokenBufferOptions? options = null)
    {
        options ??= new CurrentTokenBufferOptions();
        _maxTokenLength = options.MaxTokenLength;
    }

    public int Length => _buffer.Length;

    public bool IsEmpty => _buffer.Length == 0;

    internal string Snapshot()
    {
        return _buffer.ToString();
    }

    public TokenBufferApplyResult Apply(TokenInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Kind switch
        {
            TokenInputKind.Reset => Clear(TokenBufferResetReason.ExplicitReset),
            TokenInputKind.Uncertain => Clear(TokenBufferResetReason.UncertainInput),
            TokenInputKind.Backspace => ApplyBackspace(),
            TokenInputKind.Character => ApplyCharacter(input.Character),
            TokenInputKind.WordBoundary => CompleteToken(),
            _ => Clear(TokenBufferResetReason.UncertainInput),
        };
    }

    internal string TakeCompletedToken()
    {
        var token = _buffer.ToString();
        _buffer.Clear();
        return token;
    }

    internal void ClearBuffer()
    {
        _buffer.Clear();
    }

    internal bool TryReplaceBuffer(string originalText, string replacementText)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(replacementText);

        if (!string.Equals(_buffer.ToString(), originalText, StringComparison.Ordinal))
        {
            return false;
        }

        if (replacementText.Length > _maxTokenLength
            || replacementText.Any(static character => !char.IsLetter(character)))
        {
            return false;
        }

        _buffer.Clear();
        _buffer.Append(replacementText);
        return true;
    }

    private TokenBufferApplyResult ApplyCharacter(char character)
    {
        if (!IsTokenCharacter(character))
        {
            return Clear(TokenBufferResetReason.NonTokenCharacter);
        }

        if (_buffer.Length >= _maxTokenLength)
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

    private TokenBufferApplyResult CompleteToken()
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

    private static bool IsTokenCharacter(char character)
    {
        return char.IsLetter(character);
    }
}

public enum TokenBufferResetReason
{
    ExplicitReset,
    UncertainInput,
    NonTokenCharacter,
    MaxLengthExceeded,
    UnsafeBackspace,
}

public sealed class TokenBufferApplyResult
{
    public bool BufferChanged { get; init; }

    public bool TokenCompleted { get; init; }

    public bool BufferReset { get; init; }

    public TokenBufferResetReason ResetReason { get; init; }

    public int CompletedTokenLength { get; init; }

    public static TokenBufferApplyResult NoChange()
    {
        return new TokenBufferApplyResult();
    }

    public static TokenBufferApplyResult Changed()
    {
        return new TokenBufferApplyResult { BufferChanged = true };
    }

    public static TokenBufferApplyResult ForCompletedToken(int length)
    {
        return new TokenBufferApplyResult
        {
            BufferChanged = true,
            TokenCompleted = true,
            CompletedTokenLength = length,
        };
    }

    public static TokenBufferApplyResult Reset(TokenBufferResetReason reason, bool hadContent)
    {
        return new TokenBufferApplyResult
        {
            BufferChanged = hadContent,
            BufferReset = true,
            ResetReason = reason,
        };
    }
}
