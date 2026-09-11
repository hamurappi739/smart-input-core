using SmartInput.Core.Models;

namespace SmartInput.Core.Persistence;

public interface ISettingsPersistence
{
    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
