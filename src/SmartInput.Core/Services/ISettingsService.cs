using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public interface ISettingsService
{
    event Action? SettingsChanged;

    AppSettings Current { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default);
}
