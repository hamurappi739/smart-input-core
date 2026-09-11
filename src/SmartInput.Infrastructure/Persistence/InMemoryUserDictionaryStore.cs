namespace SmartInput.Infrastructure.Persistence;

public sealed class InMemoryUserDictionaryStore : IUserDictionaryStore
{
    private IReadOnlyList<string> _entries = Array.Empty<string>();

    public Task<IReadOnlyList<string>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_entries);
    }

    public Task SaveEntriesAsync(IReadOnlyList<string> entries, CancellationToken cancellationToken = default)
    {
        _entries = entries.ToList();
        return Task.CompletedTask;
    }
}
