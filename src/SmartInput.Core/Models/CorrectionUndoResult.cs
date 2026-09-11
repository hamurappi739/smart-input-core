namespace SmartInput.Core.Models;

public enum CorrectionUndoOutcome
{
    Success,
    NotAvailable,
    Expired,
    Blocked,
    Failed,
    AbortedByUserInput,
}

public sealed class CorrectionUndoResult
{
    public CorrectionUndoOutcome Outcome { get; init; }

    public string? FailureReason { get; init; }

    public CorrectionKind? Kind { get; init; }

    public static CorrectionUndoResult Succeeded(CorrectionKind kind)
    {
        return new CorrectionUndoResult
        {
            Outcome = CorrectionUndoOutcome.Success,
            Kind = kind,
        };
    }

    public static CorrectionUndoResult NotAvailable(string? reason = null)
    {
        return new CorrectionUndoResult
        {
            Outcome = CorrectionUndoOutcome.NotAvailable,
            FailureReason = reason,
        };
    }

    public static CorrectionUndoResult Expired()
    {
        return new CorrectionUndoResult
        {
            Outcome = CorrectionUndoOutcome.Expired,
            FailureReason = "Время для отмены истекло.",
        };
    }

    public static CorrectionUndoResult Blocked(string reason)
    {
        return new CorrectionUndoResult
        {
            Outcome = CorrectionUndoOutcome.Blocked,
            FailureReason = reason,
        };
    }

    public static CorrectionUndoResult Failed(string reason, CorrectionKind? kind = null)
    {
        return new CorrectionUndoResult
        {
            Outcome = CorrectionUndoOutcome.Failed,
            FailureReason = reason,
            Kind = kind,
        };
    }

    public static CorrectionUndoResult Aborted(CorrectionKind kind)
    {
        return new CorrectionUndoResult
        {
            Outcome = CorrectionUndoOutcome.AbortedByUserInput,
            FailureReason = "Отмена прервана: обнаружен ввод пользователя.",
            Kind = kind,
        };
    }
}
