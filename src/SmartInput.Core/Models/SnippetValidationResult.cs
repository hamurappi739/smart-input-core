namespace SmartInput.Core.Models;

public sealed class SnippetValidationResult
{
    public SnippetValidationResult(IReadOnlyList<string> errors, SnippetDefinition? normalized = null)
    {
        Errors = errors;
        Normalized = normalized;
    }

    public IReadOnlyList<string> Errors { get; }

    public SnippetDefinition? Normalized { get; }

    public bool IsValid => Errors.Count == 0 && Normalized is not null;

    public static SnippetValidationResult Success(SnippetDefinition normalized)
    {
        return new SnippetValidationResult([], normalized);
    }

    public static SnippetValidationResult Failure(params string[] errors)
    {
        return new SnippetValidationResult(errors);
    }
}
