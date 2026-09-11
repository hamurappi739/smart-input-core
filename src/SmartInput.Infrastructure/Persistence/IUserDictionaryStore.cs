namespace SmartInput.Infrastructure.Persistence;

public interface IUserDictionaryStore
{
    Task<IReadOnlyList<string>> GetEntriesAsync(CancellationToken cancellationToken = default);

    Task SaveEntriesAsync(IReadOnlyList<string> entries, CancellationToken cancellationToken = default);
}
