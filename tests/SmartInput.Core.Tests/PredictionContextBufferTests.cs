using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public class PredictionContextBufferTests
{
    [Fact]
    public void Apply_CharacterSequence_BuildsContext()
    {
        var buffer = new PredictionContextBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('h'));
        buffer.Apply(TokenInputEvent.CharacterInput('i'));

        Assert.Equal(2, buffer.CharacterCount);
        Assert.Equal(1, buffer.WordCount);
    }

    [Fact]
    public void Apply_WhitespaceBoundary_AppendsSpace()
    {
        var buffer = new PredictionContextBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('h'));
        buffer.Apply(TokenInputEvent.CharacterInput('i'));
        buffer.Apply(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        var snapshot = buffer.CreateSnapshot();
        Assert.Equal("hi ", snapshot.Context);
        Assert.Null(snapshot.CurrentWordPrefix);
    }

    [Fact]
    public void Apply_PunctuationBoundary_AppendsPunctuation()
    {
        var buffer = new PredictionContextBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('h'));
        buffer.Apply(TokenInputEvent.CharacterInput('i'));
        buffer.Apply(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemComma, 0, ',')));

        var snapshot = buffer.CreateSnapshot();
        Assert.Equal("hi,", snapshot.Context);
    }

    [Fact]
    public void Apply_MaxCharacterBound_TrimsOldestContent()
    {
        var buffer = new PredictionContextBuffer(new PredictionContextBufferOptions
        {
            MaxCharacters = 8,
            MaxWords = 8,
        });

        foreach (var character in "abcdefgh")
        {
            buffer.Apply(TokenInputEvent.CharacterInput(character));
        }

        buffer.Apply(TokenInputEvent.CharacterInput('i'));

        Assert.Equal(8, buffer.CharacterCount);
        var snapshot = buffer.CreateSnapshot();
        Assert.Equal("bcdefghi", snapshot.Context);
    }

    [Fact]
    public void Apply_MaxWordBound_TrimsOldestWords()
    {
        var buffer = new PredictionContextBuffer(new PredictionContextBufferOptions
        {
            MaxWords = 2,
            MaxCharacters = 256,
        });

        TypeWord(buffer, "one ");
        TypeWord(buffer, "two ");
        TypeWord(buffer, "three");

        Assert.Equal(2, buffer.WordCount);
        var snapshot = buffer.CreateSnapshot();
        Assert.EndsWith("three", snapshot.Context, StringComparison.Ordinal);
        Assert.DoesNotContain("one", snapshot.Context, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Backspace_RemovesLastCharacter()
    {
        var buffer = new PredictionContextBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('h'));
        buffer.Apply(TokenInputEvent.CharacterInput('i'));
        buffer.Apply(TokenInputEvent.Backspace);

        Assert.Equal(1, buffer.CharacterCount);
    }

    [Fact]
    public void Apply_BackspaceOnEmpty_ClearsBuffer()
    {
        var buffer = new PredictionContextBuffer();

        var result = buffer.Apply(TokenInputEvent.Backspace);

        Assert.True(result.BufferReset);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_NavigationBoundary_ClearsBuffer()
    {
        var buffer = new PredictionContextBuffer();

        TypeWord(buffer, "hello");
        var result = buffer.Apply(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Tab, 0)));

        Assert.True(result.BufferReset);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_Reset_ClearsBuffer()
    {
        var buffer = new PredictionContextBuffer();

        TypeWord(buffer, "hello");
        var result = buffer.Apply(TokenInputEvent.Reset);

        Assert.True(result.BufferReset);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_UncertainInput_ClearsBuffer()
    {
        var buffer = new PredictionContextBuffer();

        TypeWord(buffer, "hello");
        var result = buffer.Apply(TokenInputEvent.Uncertain);

        Assert.True(result.BufferReset);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_NonLetterCharacter_ClearsBuffer()
    {
        var buffer = new PredictionContextBuffer();

        TypeWord(buffer, "hello");
        var result = buffer.Apply(TokenInputEvent.CharacterInput('1'));

        Assert.True(result.BufferReset);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void CreateSnapshot_ExtractsCurrentWordPrefix()
    {
        var buffer = new PredictionContextBuffer();

        TypeWord(buffer, "how are ");
        buffer.Apply(TokenInputEvent.CharacterInput('y'));

        var snapshot = buffer.CreateSnapshot();
        Assert.Equal("how are y", snapshot.Context);
        Assert.Equal("y", snapshot.CurrentWordPrefix);
    }

    [Fact]
    public void ClearBuffer_RemovesAllContent()
    {
        var buffer = new PredictionContextBuffer();

        TypeWord(buffer, "hello");
        buffer.ClearBuffer(PredictionBufferResetReason.FocusChange);

        Assert.True(buffer.IsEmpty);
    }

    private static void TypeWord(PredictionContextBuffer buffer, string word)
    {
        foreach (var character in word)
        {
            if (char.IsLetter(character))
            {
                buffer.Apply(TokenInputEvent.CharacterInput(character));
            }
            else if (char.IsWhiteSpace(character))
            {
                buffer.Apply(TokenInputEvent.Boundary(
                    DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));
            }
        }
    }
}
