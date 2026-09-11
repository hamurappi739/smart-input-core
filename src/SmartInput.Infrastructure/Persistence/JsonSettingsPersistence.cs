using Microsoft.Extensions.Logging;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;

namespace SmartInput.Infrastructure.Persistence;

public sealed class JsonSettingsPersistence : ISettingsPersistence
{
    private readonly string _settingsFilePath;
    private readonly ILogger<JsonSettingsPersistence> _logger;

    public JsonSettingsPersistence(ILogger<JsonSettingsPersistence> logger)
        : this(GetDefaultSettingsPath(), logger)
    {
    }

    public JsonSettingsPersistence(string settingsFilePath, ILogger<JsonSettingsPersistence> logger)
    {
        _settingsFilePath = settingsFilePath;
        _logger = logger;
    }

    public async Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_settingsFilePath);
        var settings = await System.Text.Json.JsonSerializer
            .DeserializeAsync(stream, SmartInputPersistenceJsonContext.Default.AppSettings, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Application settings loaded from local storage.");
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_settingsFilePath);
        await System.Text.Json.JsonSerializer
            .SerializeAsync(stream, settings, SmartInputPersistenceJsonContext.Default.AppSettings, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Application settings saved to local storage.");
    }

    /// <summary>
    /// Shared location used by the resident runtime and the separately opened
    /// settings window. It contains configuration only; no typed text is
    /// persisted here.
    /// </summary>
    public static string GetDefaultSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SmartInput", "settings.json");
    }
}
