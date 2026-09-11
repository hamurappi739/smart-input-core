using SmartInput.Core.Engines;

namespace SmartInput.Core.Models;

public enum ManualCorrectionActionKind
{
    FixLayout,
    FixSpelling,
    FixText,
}

public enum ManualCorrectionStatus
{
    Success,
    NoChange,
    NoSelection,
    Blocked,
    Failed,
    Cancelled,
}

public sealed class ManualCorrectionRequest
{
    public required ManualCorrectionActionKind Action { get; init; }

    public LayoutConversionDirection? LayoutDirection { get; init; }
}

public sealed class ManualCorrectionResult
{
    public ManualCorrectionStatus Status { get; init; }

    public string? FailureReason { get; init; }

    public int CharacterCount { get; init; }

    public int TokensExamined { get; init; }

    public int TokensChanged { get; init; }

    public bool LayoutApplied { get; init; }

    public bool SpellingApplied { get; init; }

    public static ManualCorrectionResult Success(
        int characterCount,
        int tokensExamined = 0,
        int tokensChanged = 0,
        bool layoutApplied = false,
        bool spellingApplied = false)
    {
        return new ManualCorrectionResult
        {
            Status = ManualCorrectionStatus.Success,
            CharacterCount = characterCount,
            TokensExamined = tokensExamined,
            TokensChanged = tokensChanged,
            LayoutApplied = layoutApplied,
            SpellingApplied = spellingApplied,
        };
    }

    public static ManualCorrectionResult NoChange(
        int characterCount,
        int tokensExamined = 0,
        bool layoutApplied = false,
        bool spellingApplied = false)
    {
        return new ManualCorrectionResult
        {
            Status = ManualCorrectionStatus.NoChange,
            CharacterCount = characterCount,
            TokensExamined = tokensExamined,
            LayoutApplied = layoutApplied,
            SpellingApplied = spellingApplied,
        };
    }

    public static ManualCorrectionResult NoSelection()
    {
        return new ManualCorrectionResult
        {
            Status = ManualCorrectionStatus.NoSelection,
            FailureReason = "No selected text was available in the foreground application.",
        };
    }

    public static ManualCorrectionResult Blocked(string reason)
    {
        return new ManualCorrectionResult
        {
            Status = ManualCorrectionStatus.Blocked,
            FailureReason = reason,
        };
    }

    public static ManualCorrectionResult Failed(string reason)
    {
        return new ManualCorrectionResult
        {
            Status = ManualCorrectionStatus.Failed,
            FailureReason = reason,
        };
    }

    public static ManualCorrectionResult Cancelled(string reason)
    {
        return new ManualCorrectionResult
        {
            Status = ManualCorrectionStatus.Cancelled,
            FailureReason = reason,
        };
    }
}
