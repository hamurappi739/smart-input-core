using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public sealed class InMemoryCorrectionRejectionLearningStore : ICorrectionRejectionLearningStore
{
    private readonly object _sync = new();
    private readonly List<CorrectionRejectionLearningEntry> _entries = [];

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public int GetUndoCount(string candidate, string replacement, CorrectionKind kind)
    {
        lock (_sync)
        {
            var entry = _entries.FirstOrDefault(existing =>
                existing.Kind == kind
                && string.Equals(existing.Candidate, candidate, StringComparison.Ordinal)
                && string.Equals(existing.Replacement, replacement, StringComparison.Ordinal));

            return entry?.UndoCount ?? 0;
        }
    }

    public Task RemoveAsync(
        string candidate,
        string replacement,
        CorrectionKind kind,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _entries.RemoveAll(existing =>
                existing.Kind == kind
                && string.Equals(existing.Candidate, candidate, StringComparison.Ordinal)
                && string.Equals(existing.Replacement, replacement, StringComparison.Ordinal));
        }

        return Task.CompletedTask;
    }

    public Task RecordRejectionAsync(
        CorrectionRejectionLearningEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_sync)
        {
            var index = _entries.FindIndex(existing =>
                existing.Kind == entry.Kind
                && string.Equals(existing.Candidate, entry.Candidate, StringComparison.Ordinal)
                && string.Equals(existing.Replacement, entry.Replacement, StringComparison.Ordinal));

            if (index >= 0)
            {
                var existing = _entries[index];
                _entries[index] = new CorrectionRejectionLearningEntry
                {
                    Candidate = existing.Candidate,
                    Replacement = existing.Replacement,
                    Kind = existing.Kind,
                    UndoCount = existing.UndoCount + entry.UndoCount,
                    RejectionWeight = entry.RejectionWeight ?? existing.RejectionWeight,
                };
            }
            else
            {
                _entries.Add(entry);
            }

            _entries.Sort(static (left, right) =>
            {
                var kindComparison = left.Kind.CompareTo(right.Kind);
                if (kindComparison != 0)
                {
                    return kindComparison;
                }

                var candidateComparison = string.Compare(
                    left.Candidate,
                    right.Candidate,
                    StringComparison.Ordinal);
                if (candidateComparison != 0)
                {
                    return candidateComparison;
                }

                return string.Compare(left.Replacement, right.Replacement, StringComparison.Ordinal);
            });
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CorrectionRejectionLearningEntry>> GetEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<CorrectionRejectionLearningEntry>>(_entries.ToList());
        }
    }
}
