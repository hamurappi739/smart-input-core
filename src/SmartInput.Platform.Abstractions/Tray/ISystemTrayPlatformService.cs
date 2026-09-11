namespace SmartInput.Platform.Abstractions.Tray;

public interface ISystemTrayPlatformService
{
    event Action<SystemTrayMenuAction>? MenuActionRequested;

    Task<bool> TryShowAsync(CancellationToken cancellationToken = default);

    Task HideAsync(CancellationToken cancellationToken = default);

    Task UpdateMenuStateAsync(SystemTrayMenuState state, CancellationToken cancellationToken = default);

    Task SetTooltipAsync(string tooltip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows a short, text-free-of-user-input desktop notification through the
    /// resident tray icon. Implementations without a desktop notification
    /// surface may safely ignore it.
    /// </summary>
    Task ShowNotificationAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
