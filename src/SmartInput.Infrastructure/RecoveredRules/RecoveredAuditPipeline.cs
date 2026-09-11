namespace SmartInput.Infrastructure.RecoveredRules;

/// <summary>
/// The only decision an unverified recovered model is allowed to emit.
/// This type intentionally has no Apply value.
/// </summary>
public enum RecoveredAuditDecision
{
    Wait,
}

/// <summary>
/// Structural observation of the statically confirmed boundary route.
/// It contains no replacement candidate and no decision beyond Wait.
/// </summary>
public sealed record RecoveredBoundaryAuditObservation(
    RecoveredBoundaryPreparationStatus Status,
    int DescriptorRva,
    int TrimmedByteLength,
    byte[]? FinalMatcherBytes,
    RecoveredAuditDecision Decision,
    string Reason)
{
    public bool HasFinalMatcherBytes => FinalMatcherBytes is not null;

    public byte[]? CopyFinalMatcherBytes() => FinalMatcherBytes?.ToArray();

    public bool CanApply => false;
}

/// <summary>
/// Audit-only entry point for recovered Caramba artifacts.
///
/// It does not call a matcher or expose an Apply operation. Boundary output
/// is available because the two static descriptor streams and the formatter
/// argument were resolved offline.
/// </summary>
public sealed class RecoveredAuditPipeline
{
    public RecoveredBoundaryAuditObservation ObserveBoundary(
        ReadOnlySpan<byte> source,
        int mode = 0)
    {
        var preparation = RecoveredBoundaryPreparation.Analyze(source, mode);
        return new RecoveredBoundaryAuditObservation(
            preparation.Status,
            preparation.DescriptorRva,
            preparation.CopyTrimmedBytes().Length,
            preparation.CopyFinalMatcherBytes(),
            RecoveredAuditDecision.Wait,
            preparation.Status switch
            {
                RecoveredBoundaryPreparationStatus.EmptyInput => "Input is empty.",
                RecoveredBoundaryPreparationStatus.InvalidUtf8 => "Input is not valid UTF-8.",
                _ => "Final descriptor-writer bytes and model semantics are not proved.",
            });
    }
}
