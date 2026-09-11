namespace SmartInput.Core.Services;

public sealed class TextReplacementValidationResult
{
    public bool IsValid { get; init; }

    public string? FailureReason { get; init; }

    public static TextReplacementValidationResult Valid()
    {
        return new TextReplacementValidationResult { IsValid = true };
    }

    public static TextReplacementValidationResult Invalid(string failureReason)
    {
        return new TextReplacementValidationResult
        {
            IsValid = false,
            FailureReason = failureReason,
        };
    }
}

public static class TextReplacementValidator
{
    public const int MaxTextLength = 128;

    public static TextReplacementValidationResult Validate(string originalText, string replacementText)
    {
        if (string.IsNullOrWhiteSpace(originalText))
        {
            return TextReplacementValidationResult.Invalid("Original text must not be empty.");
        }

        if (replacementText is null)
        {
            return TextReplacementValidationResult.Invalid("Replacement text must not be null.");
        }

        if (originalText.Length > MaxTextLength || replacementText.Length > MaxTextLength)
        {
            return TextReplacementValidationResult.Invalid("Replacement text exceeds the allowed length.");
        }

        if (originalText.Any(char.IsControl) || replacementText.Any(char.IsControl))
        {
            return TextReplacementValidationResult.Invalid("Control characters are not supported.");
        }

        return TextReplacementValidationResult.Valid();
    }
}
