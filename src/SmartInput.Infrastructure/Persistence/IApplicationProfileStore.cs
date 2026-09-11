using SmartInput.Core.Models;

namespace SmartInput.Infrastructure.Persistence;

public interface IApplicationProfileStore
{
    Task<IReadOnlyList<ApplicationProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);

    Task SaveProfilesAsync(IReadOnlyList<ApplicationProfile> profiles, CancellationToken cancellationToken = default);
}
