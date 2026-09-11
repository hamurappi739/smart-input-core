namespace SmartInput.Platform.Abstractions.Input;

public enum KeyboardInputLanguage
{
    English,
    Russian,
}

/// <summary>
/// Requests an input-language change in the foreground application. A failed
/// request must not affect text correction itself.
/// </summary>
public interface IKeyboardInputLanguageService
{
    Task<bool> SetForegroundInputLanguageAsync(
        KeyboardInputLanguage language,
        CancellationToken cancellationToken = default);
}
