namespace SmartInput.Platform.Abstractions.Hotkeys;

public interface IGlobalHotkeyService : IAsyncDisposable
{
    event EventHandler<GlobalHotkeyPressedEventArgs>? HotkeyPressed;

    Task<GlobalHotkeyRegistrationResult> RegisterAsync(
        string hotkeyId,
        uint modifiers,
        uint virtualKey,
        string displayName,
        CancellationToken cancellationToken = default);

    Task UnregisterAsync(string hotkeyId, CancellationToken cancellationToken = default);

    Task UnregisterAllAsync(CancellationToken cancellationToken = default);

    GlobalHotkeyRegistrationResult? GetRegistration(string hotkeyId);
}
