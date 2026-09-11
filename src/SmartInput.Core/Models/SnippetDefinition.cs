namespace SmartInput.Core.Models;

public sealed class SnippetDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Trigger { get; init; }

    public required string Replacement { get; init; }

    public TypingLanguage? Language { get; init; }

    public bool CaseSensitive { get; init; }

    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<string> EnabledApplications { get; init; } = [];

    public IReadOnlyList<string> DisabledApplications { get; init; } = [];
}
