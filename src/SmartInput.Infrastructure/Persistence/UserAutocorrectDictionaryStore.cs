using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public sealed class UserAutocorrectDictionaryStore : IUserAutocorrectDictionaryStore
{
    private readonly IUserAutocorrectDictionaryPersistence _persistence;
    private readonly object _sync = new();
    private List<UserAutocorrectDictionaryEntry> _entries = [];

    public UserAutocorrectDictionaryStore(IUserAutocorrectDictionaryPersistence persistence)
    {
        _persistence = persistence;
    }

    public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToList();
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _entries = NormalizeEntries(loaded);
        }
    }

    public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!AutocorrectDictionaryNormalizer.TryNormalizeEntry(entry, out var normalizedEntry))
        {
            throw new ArgumentException("Dictionary entry is invalid.", nameof(entry));
        }

        lock (_sync)
        {
            var lookupKey = AutocorrectDictionaryNormalizer.NormalizeLookupKey(normalizedEntry.Word);
            var alreadyExists = _entries.Any(existing =>
                existing.Language == normalizedEntry.Language
                && string.Equals(
                    AutocorrectDictionaryNormalizer.NormalizeLookupKey(existing.Word),
                    lookupKey,
                    StringComparison.Ordinal));
            if (!alreadyExists
                && _entries.Count >= AutocorrectDictionaryNormalizer.MaxUserDictionaryEntries)
            {
                throw new InvalidOperationException("User dictionary safety limit was reached.");
            }

            _entries.RemoveAll(existing =>
                existing.Language == normalizedEntry.Language
                && string.Equals(
                    AutocorrectDictionaryNormalizer.NormalizeLookupKey(existing.Word),
                    lookupKey,
                    StringComparison.Ordinal));

            _entries.Add(normalizedEntry);
            _entries = _entries
                .OrderBy(existing => existing.Language)
                .ThenBy(existing => AutocorrectDictionaryNormalizer.NormalizeLookupKey(existing.Word), StringComparer.Ordinal)
                .ToList();
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
    {
        if (!AutocorrectDictionaryNormalizer.TryNormalizeEntryWord(word, out var lookupKey))
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            _entries.RemoveAll(existing =>
                existing.Language == language
                && string.Equals(
                    AutocorrectDictionaryNormalizer.NormalizeLookupKey(existing.Word),
                    lookupKey,
                    StringComparison.Ordinal));
        }

        return Task.CompletedTask;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UserAutocorrectDictionaryEntry> snapshot;
        lock (_sync)
        {
            snapshot = _entries.ToList();
        }

        await _persistence.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static List<UserAutocorrectDictionaryEntry> NormalizeEntries(
        IReadOnlyList<UserAutocorrectDictionaryEntry> entries)
    {
        var normalized = new Dictionary<(TypingLanguage Language, string LookupKey), UserAutocorrectDictionaryEntry>();

        foreach (var entry in entries)
        {
            if (!AutocorrectDictionaryNormalizer.TryNormalizeEntry(entry, out var normalizedEntry))
            {
                continue;
            }

            var lookupKey = AutocorrectDictionaryNormalizer.NormalizeLookupKey(normalizedEntry.Word);
            var key = (normalizedEntry.Language, lookupKey);
            if (!normalized.ContainsKey(key)
                && normalized.Count >= AutocorrectDictionaryNormalizer.MaxUserDictionaryEntries)
            {
                continue;
            }

            normalized[key] = normalizedEntry;
        }

        return normalized.Values
            .OrderBy(entry => entry.Language)
            .ThenBy(entry => AutocorrectDictionaryNormalizer.NormalizeLookupKey(entry.Word), StringComparer.Ordinal)
            .ToList();
    }
}
