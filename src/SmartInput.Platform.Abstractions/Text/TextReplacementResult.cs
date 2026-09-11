namespace SmartInput.Platform.Abstractions.Text;

public sealed class TextReplacementResult
{
    public TextReplacementStatus Status { get; init; }

    public string? FailureReason { get; init; }

    public int DeletedCharacterCount { get; init; }

    public int InsertedCharacterCount { get; init; }

    public static TextReplacementResult Success(int deletedCharacterCount, int insertedCharacterCount)
    {
        return new TextReplacementResult
        {
            Status = TextReplacementStatus.Success,
            DeletedCharacterCount = deletedCharacterCount,
            InsertedCharacterCount = insertedCharacterCount,
        };
    }

    public static TextReplacementResult Failed(string failureReason)
    {
        return new TextReplacementResult
        {
            Status = TextReplacementStatus.Failed,
            FailureReason = failureReason,
        };
    }

    public static TextReplacementResult Blocked(string failureReason)
    {
        return new TextReplacementResult
        {
            Status = TextReplacementStatus.Blocked,
            FailureReason = failureReason,
        };
    }

    public static TextReplacementResult AbortedByUserInput(int deletedCharacterCount, int insertedCharacterCount)
    {
        return new TextReplacementResult
        {
            Status = TextReplacementStatus.AbortedByUserInput,
            FailureReason = "Replacement aborted because genuine user input was detected.",
            DeletedCharacterCount = deletedCharacterCount,
            InsertedCharacterCount = insertedCharacterCount,
        };
    }
}
