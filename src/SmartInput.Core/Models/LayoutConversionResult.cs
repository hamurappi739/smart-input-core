namespace SmartInput.Core.Models;

public enum LayoutConversionStatus
{
    Success,
    Blocked,
    Failed,
    NoSelection,
    NotSupported,
}

public sealed class LayoutConversionResult
{
    public LayoutConversionStatus Status { get; init; }

    public string? FailureReason { get; init; }

    public int CharacterCount { get; init; }

    public static LayoutConversionResult Success(int characterCount)
    {
        return new LayoutConversionResult
        {
            Status = LayoutConversionStatus.Success,
            CharacterCount = characterCount,
        };
    }

    public static LayoutConversionResult Blocked(string failureReason)
    {
        return new LayoutConversionResult
        {
            Status = LayoutConversionStatus.Blocked,
            FailureReason = failureReason,
        };
    }

    public static LayoutConversionResult Failed(string failureReason)
    {
        return new LayoutConversionResult
        {
            Status = LayoutConversionStatus.Failed,
            FailureReason = failureReason,
        };
    }

    public static LayoutConversionResult NoSelection()
    {
        return new LayoutConversionResult
        {
            Status = LayoutConversionStatus.NoSelection,
            FailureReason = "В активном приложении нет доступного выделенного текста.",
        };
    }
}
