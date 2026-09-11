using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public sealed class CorrectionRejectionLearningStore : ICorrectionRejectionLearningStore
{
    private readonly ICorrectionRejectionLearningPersistence _persistence;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private List<CorrectionRejectionLearningEntry> _entries = [];
    private bool _loaded;

    public CorrectionRejectionLearningStore(ICorrectionRejectionLearningPersistence persistence)
    {
        _persistence = persistence;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedInternalAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var loadedEntries = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _entries = loadedEntries.ToList();
            _loaded = true;
        }
    }

    public int GetUndoCount(string candidate, string replacement, CorrectionKind kind)
    {
        if (!_loaded)
        {
            return 0;
        }

        lock (_sync)
        {
            var entry = _entries.FirstOrDefault(existing =>
                existing.Kind == kind
                && string.Equals(existing.Candidate, candidate, StringComparison.Ordinal)
                && string.Equals(existing.Replacement, replacement, StringComparison.Ordinal));

            return entry?.UndoCount ?? 0;
        }
    }

    public async Task RemoveAsync(
        string candidate,
        string replacement,
        CorrectionKind kind,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedInternalAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _entries.RemoveAll(existing =>
                existing.Kind == kind
                && string.Equals(existing.Candidate, candidate, StringComparison.Ordinal)
                && string.Equals(existing.Replacement, replacement, StringComparison.Ordinal));
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordRejectionAsync(
        CorrectionRejectionLearningEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await EnsureLoadedInternalAsync(cancellationToken).ConfigureAwait(false);

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

            _entries = _entries
                .OrderBy(static item => item.Kind)
                .ThenBy(static item => item.Candidate, StringComparer.Ordinal)
                .ThenBy(static item => item.Replacement, StringComparer.Ordinal)
                .ToList();
        }

        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CorrectionRejectionLearningEntry>> GetEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedInternalAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            return _entries.ToList();
        }
    }

    private async Task EnsureLoadedInternalAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        var loadedEntries = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            if (_loaded)
            {
                return;
            }

            _entries = loadedEntries.ToList();
            _loaded = true;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _persistGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<CorrectionRejectionLearningEntry> snapshot;
            lock (_sync)
            {
                snapshot = _entries.ToList();
            }

            await _persistence.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _persistGate.Release();
        }
    }
}
