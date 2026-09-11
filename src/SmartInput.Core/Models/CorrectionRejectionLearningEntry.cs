namespace SmartInput.Core.Models;

public sealed class CorrectionRejectionLearningEntry
{
    public required string Candidate { get; init; }

    public required string Replacement { get; init; }

    public CorrectionKind Kind { get; init; }

    public int UndoCount { get; init; }

    public double? RejectionWeight { get; init; }
}
