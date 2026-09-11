namespace SmartInput.Core.Models;

public sealed class ApplicationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public bool SafeModeEnabled { get; set; }

    public bool AutomaticLayoutEnabled { get; set; } = true;

    public bool AutocorrectEnabled { get; set; } = true;

    public bool PredictionEnabled { get; set; } = true;
}
