using SmartInput.Core.Services;

namespace SmartInput.App.ViewModels;

public abstract class SettingsViewModelBase : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private bool _isSyncing;

    protected SettingsViewModelBase(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.SettingsChanged += HandleSettingsChanged;
        _ = InitializeAsync();
    }

    protected ISettingsService SettingsService => _settingsService;

    protected bool IsSyncing => _isSyncing;

    protected async Task InitializeAsync()
    {
        await _settingsService.LoadAsync().ConfigureAwait(true);
        BeginSync();
        SyncFromSettings(_settingsService.Current);
        EndSync();
        OnSettingsLoaded();
    }

    protected virtual void OnSettingsLoaded()
    {
    }

    protected virtual void OnExternalSettingsChanged()
    {
    }

    private void HandleSettingsChanged()
    {
        BeginSync();
        SyncFromSettings(_settingsService.Current);
        EndSync();
        OnExternalSettingsChanged();
    }

    protected void BeginSync()
    {
        _isSyncing = true;
    }

    protected void EndSync()
    {
        _isSyncing = false;
    }

    protected void PersistSetting(Action<Core.Models.AppSettings> update)
    {
        if (_isSyncing)
        {
            return;
        }

        _ = _settingsService.UpdateAsync(update);
    }

    protected abstract void SyncFromSettings(Core.Models.AppSettings settings);
}
