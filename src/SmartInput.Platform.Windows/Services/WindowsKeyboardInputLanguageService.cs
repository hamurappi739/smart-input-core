using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Windows.Native;

namespace SmartInput.Platform.Windows.Services;

public sealed class WindowsKeyboardInputLanguageService : IKeyboardInputLanguageService
{
    private const ushort EnglishLanguageIdentifier = 0x0409;
    private const ushort RussianLanguageIdentifier = 0x0419;

    public Task<bool> SetForegroundInputLanguageAsync(
        KeyboardInputLanguage language,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var window = Win32Window.GetForegroundWindow();
        if (window == 0)
        {
            return Task.FromResult(false);
        }

        var targetLanguageIdentifier = language == KeyboardInputLanguage.English
            ? EnglishLanguageIdentifier
            : RussianLanguageIdentifier;
        var layout = FindInstalledLayout(targetLanguageIdentifier);
        if (layout == 0)
        {
            return Task.FromResult(false);
        }

        var posted = Win32Keyboard.PostMessage(
            window,
            Win32Keyboard.WmInputLangChangeRequest,
            0,
            layout);
        return Task.FromResult(posted);
    }

    private static nint FindInstalledLayout(ushort languageIdentifier)
    {
        var count = Win32Keyboard.GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return 0;
        }

        var layouts = new nint[count];
        var received = Win32Keyboard.GetKeyboardLayoutList(layouts.Length, layouts);
        for (var index = 0; index < received; index++)
        {
            var layout = layouts[index];
            if (((ushort)((nuint)layout & 0xFFFF)) == languageIdentifier)
            {
                return layout;
            }
        }

        return 0;
    }
}
