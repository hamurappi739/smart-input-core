using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Services;

public enum PredictionBufferResetReason
{
    ExplicitReset,
    UncertainInput,
    NonLetterCharacter,
    NavigationBoundary,
    UnsupportedBoundary,
    UnsafeBackspace,
    FocusChange,
    PolicyBlocked,
    Disabled,
    ManualReset,
}

public sealed class PredictionBufferApplyResult
{
    public bool BufferChanged { get; init; }

    public bool BufferReset { get; init; }

    public PredictionBufferResetReason ResetReason { get; init; }

    public static PredictionBufferApplyResult NoChange()
    {
        return new PredictionBufferApplyResult();
    }

    public static PredictionBufferApplyResult Changed()
    {
        return new PredictionBufferApplyResult { BufferChanged = true };
    }

    public static PredictionBufferApplyResult Reset(PredictionBufferResetReason reason, bool hadContent)
    {
        return new PredictionBufferApplyResult
        {
            BufferChanged = hadContent,
            BufferReset = true,
            ResetReason = reason,
        };
    }
}

internal sealed class PredictionContextSnapshot
{
    public required string Context { get; init; }

    public string? CurrentWordPrefix { get; init; }

    public int WordCount { get; init; }

    public int CharacterCount { get; init; }
}

public sealed class PredictionContextBuffer
{
    private readonly int _maxWords;
    private readonly int _maxCharacters;
    private readonly StringBuilder _text = new();

    public PredictionContextBuffer(PredictionContextBufferOptions? options = null)
    {
        options ??= new PredictionContextBufferOptions();
        _maxWords = options.MaxWords;
        _maxCharacters = options.MaxCharacters;
    }

    public int CharacterCount => _text.Length;

    public int WordCount => CountWords(_text);

    public bool IsEmpty => _text.Length == 0;

    public PredictionBufferApplyResult Apply(TokenInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Kind switch
        {
            TokenInputKind.Reset => Clear(PredictionBufferResetReason.ExplicitReset),
            TokenInputKind.Uncertain => Clear(PredictionBufferResetReason.UncertainInput),
            TokenInputKind.Backspace => ApplyBackspace(),
            TokenInputKind.Character => ApplyCharacter(input.Character),
            TokenInputKind.WordBoundary => ApplyBoundary(input.DeferredBoundary),
            _ => Clear(PredictionBufferResetReason.UncertainInput),
        };
    }

    public void ClearBuffer(PredictionBufferResetReason reason)
    {
        _ = Clear(reason);
    }

    internal PredictionContextSnapshot CreateSnapshot()
    {
        var context = _text.ToString();
        return new PredictionContextSnapshot
        {
            Context = context,
            CurrentWordPrefix = ExtractCurrentWordPrefix(context),
            WordCount = CountWords(context),
            CharacterCount = context.Length,
        };
    }

    private PredictionBufferApplyResult ApplyCharacter(char character)
    {
        if (!char.IsLetter(character))
        {
            return Clear(PredictionBufferResetReason.NonLetterCharacter);
        }

        _text.Append(character);
        TrimToBounds();
        return PredictionBufferApplyResult.Changed();
    }

    private PredictionBufferApplyResult ApplyBoundary(DeferredBoundaryKey? boundary)
    {
        if (PredictionBoundaryCharacterResolver.IsNavigationBoundary(boundary))
        {
            return Clear(PredictionBufferResetReason.NavigationBoundary);
        }

        var boundaryCharacter = PredictionBoundaryCharacterResolver.TryGetAppendableBoundaryCharacter(boundary);
        if (boundaryCharacter is null)
        {
            return Clear(PredictionBufferResetReason.UnsupportedBoundary);
        }

        _text.Append(boundaryCharacter.Value);
        TrimToBounds();
        return PredictionBufferApplyResult.Changed();
    }

    private PredictionBufferApplyResult ApplyBackspace()
    {
        if (_text.Length == 0)
        {
            return Clear(PredictionBufferResetReason.UnsafeBackspace);
        }

        _text.Length--;
        return PredictionBufferApplyResult.Changed();
    }

    private PredictionBufferApplyResult Clear(PredictionBufferResetReason reason)
    {
        var hadContent = _text.Length > 0;
        _text.Clear();
        return PredictionBufferApplyResult.Reset(reason, hadContent);
    }

    private void TrimToBounds()
    {
        if (_text.Length > _maxCharacters)
        {
            _text.Remove(0, _text.Length - _maxCharacters);
        }

        while (CountWords(_text) > _maxWords)
        {
            RemoveLeadingWord();
        }
    }

    private void RemoveLeadingWord()
    {
        var index = 0;
        while (index < _text.Length && !char.IsLetter(_text[index]))
        {
            index++;
        }

        while (index < _text.Length && char.IsLetter(_text[index]))
        {
            index++;
        }

        if (index >= _text.Length)
        {
            _text.Clear();
            return;
        }

        _text.Remove(0, index);
    }

    private static string? ExtractCurrentWordPrefix(string context)
    {
        if (string.IsNullOrEmpty(context) || char.IsWhiteSpace(context[^1]))
        {
            return null;
        }

        var end = context.Length - 1;
        while (end >= 0 && char.IsLetter(context[end]))
        {
            end--;
        }

        var prefix = context[(end + 1)..];
        return string.IsNullOrEmpty(prefix) ? null : prefix;
    }

    private static int CountWords(StringBuilder text)
    {
        return CountWords(text.ToString());
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;

        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return count;
    }
}
