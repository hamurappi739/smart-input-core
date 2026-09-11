using SmartInput.Core.Models;

namespace SmartInput.Core.Persistence;

public interface IUserAutocorrectDictionaryPersistence
{
    Task<IReadOnlyList<UserAutocorrectDictionaryEntry>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyList<UserAutocorrectDictionaryEntry> entries,
        CancellationToken cancellationToken = default);
}
