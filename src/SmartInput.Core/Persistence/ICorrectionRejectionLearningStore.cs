using SmartInput.Core.Models;

namespace SmartInput.Core.Persistence;

public interface ICorrectionRejectionLearningStore
{
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads local learning data after it was changed by a separately
    /// running settings window. Implementations that have no external backing
    /// store can retain their current in-memory snapshot.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        return EnsureLoadedAsync(cancellationToken);
    }

    int GetUndoCount(string candidate, string replacement, CorrectionKind kind);

    Task RecordRejectionAsync(
        CorrectionRejectionLearningEntry entry,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CorrectionRejectionLearningEntry>> GetEntriesAsync(
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string candidate,
        string replacement,
        CorrectionKind kind,
        CancellationToken cancellationToken = default);
}
