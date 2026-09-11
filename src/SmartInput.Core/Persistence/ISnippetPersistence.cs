using SmartInput.Core.Models;

namespace SmartInput.Core.Persistence;

public interface ISnippetPersistence
{
    Task<IReadOnlyList<SnippetDefinition>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<SnippetDefinition> snippets, CancellationToken cancellationToken = default);
}
