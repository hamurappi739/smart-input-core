using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Integration;

/// <summary>
/// Converts bounded language statistics into the only context representation
/// allowed across the optional Rust shadow boundary. No previous words or raw
/// text are included.
/// </summary>
public static class RustShadowContextFormatter
{
    public static string FromPortableRequest(PortableCorrectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var language = request.DominantLanguage?.Trim().ToLowerInvariant() switch
        {
            "ru" or "russian" => TypingLanguage.Russian,
            "en" or "english" => TypingLanguage.English,
            _ => (TypingLanguage?)null,
        };

        return FromHint(new SentenceLanguageHint(
            language,
            Math.Max(0, request.RussianTokenCount),
            Math.Max(0, request.EnglishTokenCount),
            Math.Max(0, request.ContextTokenCount),
            Math.Max(0, request.ContextCharacterCount)));
    }

    public static string FromHint(SentenceLanguageHint hint)
    {
        var language = hint.DominantLanguage switch
        {
            TypingLanguage.Russian => "ru",
            TypingLanguage.English => "en",
            _ => "unknown",
        };

        return $"lang={language};ru={Math.Max(0, hint.RussianTokenCount)};"
            + $"en={Math.Max(0, hint.EnglishTokenCount)};"
            + $"tokens={Math.Max(0, hint.TokenCount)};"
            + $"chars={Math.Max(0, hint.CharacterCount)}";
    }
}
