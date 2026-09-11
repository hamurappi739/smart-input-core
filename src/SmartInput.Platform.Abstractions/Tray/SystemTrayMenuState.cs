namespace SmartInput.Platform.Abstractions.Tray;

public sealed class SystemTrayMenuState
{
    public bool IsProtectionEnabled { get; init; }

    public bool IsAutomaticLayoutEnabled { get; init; }

    public bool IsAutocorrectEnabled { get; init; }

    public bool IsPredictionEnabled { get; init; }

    public bool IsSnippetsEnabled { get; init; }

    public bool IsEmergencyPaused { get; init; }
}
