namespace SmartInput.Platform.Abstractions.Hotkeys;

public enum GlobalHotkeyRegistrationState
{
    NotRegistered,
    Registered,
    InvalidBinding,
    Conflict,
    Failed,
}

public sealed class GlobalHotkeyRegistrationResult
{
    public GlobalHotkeyRegistrationState State { get; init; }

    public string HotkeyId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsSuccess => State == GlobalHotkeyRegistrationState.Registered;

    public static GlobalHotkeyRegistrationResult Registered(string hotkeyId, string displayName)
    {
        return new GlobalHotkeyRegistrationResult
        {
            State = GlobalHotkeyRegistrationState.Registered,
            HotkeyId = hotkeyId,
            DisplayName = displayName,
        };
    }

    public static GlobalHotkeyRegistrationResult Conflict(string hotkeyId, string displayName, string message)
    {
        return new GlobalHotkeyRegistrationResult
        {
            State = GlobalHotkeyRegistrationState.Conflict,
            HotkeyId = hotkeyId,
            DisplayName = displayName,
            ErrorMessage = message,
        };
    }

    public static GlobalHotkeyRegistrationResult Invalid(string hotkeyId, string? displayName, string message)
    {
        return new GlobalHotkeyRegistrationResult
        {
            State = GlobalHotkeyRegistrationState.InvalidBinding,
            HotkeyId = hotkeyId,
            DisplayName = displayName,
            ErrorMessage = message,
        };
    }

    public static GlobalHotkeyRegistrationResult Failed(string hotkeyId, string? displayName, string message)
    {
        return new GlobalHotkeyRegistrationResult
        {
            State = GlobalHotkeyRegistrationState.Failed,
            HotkeyId = hotkeyId,
            DisplayName = displayName,
            ErrorMessage = message,
        };
    }

    public static GlobalHotkeyRegistrationResult Unassigned(string hotkeyId)
    {
        return new GlobalHotkeyRegistrationResult
        {
            State = GlobalHotkeyRegistrationState.NotRegistered,
            HotkeyId = hotkeyId,
        };
    }
}

public sealed class GlobalHotkeyPressedEventArgs : EventArgs
{
    public required string HotkeyId { get; init; }
}
