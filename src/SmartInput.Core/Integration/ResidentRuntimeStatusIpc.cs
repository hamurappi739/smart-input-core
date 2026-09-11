using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Diagnostics;

namespace SmartInput.Core.Integration;

/// <summary>
/// Aggregate-only health snapshot exposed by the resident keyboard process.
/// It deliberately contains no token, replacement text, application identity,
/// title or other user-input data.
/// </summary>
public sealed record ResidentRuntimeStatusSnapshot(
    bool IsMonitoring,
    LiveLayoutCorrectionStatus CorrectionStatus,
    LivePipelineStageSnapshot LivePipeline);

/// <summary>
/// Reads the resident process status without sending any request payload.
/// Implementations fail closed when the resident process is not running.
/// </summary>
public interface IResidentRuntimeStatusReader
{
    Task<ResidentRuntimeStatusSnapshot?> TryReadAsync(CancellationToken cancellationToken = default);
}

public static class ResidentRuntimeStatusIpc
{
    public const string PipeName = "SmartInput.ResidentRuntimeStatus.v1";
}
