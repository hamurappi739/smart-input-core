using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Safety;

namespace SmartInput.Infrastructure.Safety;

public sealed class SettingsBackedEmergencyPauseService : IEmergencyPauseService
{
    private readonly ISettingsService _settingsService;

    public SettingsBackedEmergencyPauseService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public bool IsPaused => _settingsService.Current.EmergencyPauseEnabled;

    public void Pause()
    {
        _ = _settingsService.UpdateAsync(settings => settings.EmergencyPauseEnabled = true);
    }

    public void Resume()
    {
        _ = _settingsService.UpdateAsync(settings => settings.EmergencyPauseEnabled = false);
    }
}
