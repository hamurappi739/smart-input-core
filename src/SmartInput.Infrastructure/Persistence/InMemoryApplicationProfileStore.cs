using SmartInput.Core.Models;

namespace SmartInput.Infrastructure.Persistence;

public sealed class InMemoryApplicationProfileStore : IApplicationProfileStore
{
    private IReadOnlyList<ApplicationProfile> _profiles = Array.Empty<ApplicationProfile>();

    public Task<IReadOnlyList<ApplicationProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_profiles);
    }

    public Task SaveProfilesAsync(IReadOnlyList<ApplicationProfile> profiles, CancellationToken cancellationToken = default)
    {
        _profiles = profiles.ToList();
        return Task.CompletedTask;
    }
}
