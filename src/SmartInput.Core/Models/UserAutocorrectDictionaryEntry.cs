namespace SmartInput.Core.Models;

public sealed class UserAutocorrectDictionaryEntry
{
    public required string Word { get; init; }

    public TypingLanguage Language { get; init; }

    public double? Frequency { get; init; }

    public bool NeverAutocorrect { get; init; }
}
