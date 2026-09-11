using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsPersistence _persistence;

    public SettingsService(ISettingsPersistence persistence)
    {
        _persistence = persistence;
        Current = SettingsDefaults.CreateDefault();
    }

    public event Action? SettingsChanged;

    public AppSettings Current { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _persistence.LoadAsync(cancellationToken).ConfigureAwait(false);
        Current = loaded ?? SettingsDefaults.CreateDefault();
        SettingsChanged?.Invoke();
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        return _persistence.SaveAsync(Current, cancellationToken);
    }

    public async Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        update(Current);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        SettingsChanged?.Invoke();
    }
}
