using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

public interface IUserAutocorrectDictionaryStore
{
    IReadOnlyList<UserAutocorrectDictionaryEntry> Entries { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default);

    Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
