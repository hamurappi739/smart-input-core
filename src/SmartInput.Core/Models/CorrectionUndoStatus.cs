namespace SmartInput.Core.Models;

public sealed class CorrectionUndoStatus
{
    public bool IsAvailable { get; init; }

    public CorrectionKind? PendingKind { get; init; }

    public int UndoAttempts { get; init; }

    public int UndoSucceeded { get; init; }

    public int UndoBlocked { get; init; }

    public int UndoFailed { get; init; }

    public int UndoInvalidated { get; init; }

    public int LearningRejectionsRecorded { get; init; }
}
