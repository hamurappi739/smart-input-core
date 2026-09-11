using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public class InputCharacterClassificationTests
{
    [Theory]
    [InlineData('/')]
    [InlineData('@')]
    [InlineData('_')]
    [InlineData('-')]
    [InlineData('a')]
    [InlineData('с')]
    [InlineData('1')]
    public void IsTrackableTriggerCharacter_AllowsSnippetSymbols(char character)
    {
        Assert.True(InputCharacterClassification.IsTrackableTriggerCharacter(character));
        Assert.False(InputCharacterClassification.IsWordBoundaryCharacter(character));
    }

    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData('!')]
    [InlineData(' ')]
    [InlineData('\t')]
    [InlineData('\n')]
    public void IsWordBoundaryCharacter_EndsTriggers(char character)
    {
        Assert.True(InputCharacterClassification.IsWordBoundaryCharacter(character));
        Assert.False(InputCharacterClassification.IsTrackableTriggerCharacter(character));
    }
}
