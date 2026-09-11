namespace SmartInput.Core.Models;

public enum SnippetMatchStatus
{
    NoMatch,
    Matched,
    SnippetDisabled,
    LanguageMismatch,
    ApplicationFiltered,
}

public sealed class SnippetMatchContext
{
    public TypingLanguage? Language { get; init; }

    public string? ApplicationProcessName { get; init; }
}

public sealed class SnippetMatchResult
{
    public SnippetMatchStatus Status { get; init; }

    public SnippetDefinition? Snippet { get; init; }

    public string? Replacement { get; init; }

    public bool IsMatch => Status == SnippetMatchStatus.Matched && !string.IsNullOrEmpty(Replacement);

    public static SnippetMatchResult NoMatch()
    {
        return new SnippetMatchResult { Status = SnippetMatchStatus.NoMatch };
    }

    public static SnippetMatchResult Matched(SnippetDefinition snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        return new SnippetMatchResult
        {
            Status = SnippetMatchStatus.Matched,
            Snippet = snippet,
            Replacement = snippet.Replacement,
        };
    }
}
