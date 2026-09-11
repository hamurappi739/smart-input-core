using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// A tiny, explicit safety net for high-signal typos that are otherwise easy
/// to suppress as ambiguous because several one-edit dictionary neighbours
/// exist. The normal one-edit engine remains the primary path; these entries
/// are only used when the exact source is not a known word.
/// </summary>
internal static class CommonTypoCorrections
{
    private static readonly IReadOnlyDictionary<string, string> Russian =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["кмнда"] = "команда",
            ["пжлуста"] = "пожалуйста",
            ["пжлст"] = "пожалуйста",
            ["пожалста"] = "пожалуйста",
            ["спсибо"] = "спасибо",
        };

    private static readonly IReadOnlyDictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["teh"] = "the",
            ["adn"] = "and",
            ["helo"] = "hello",
        };

    internal static bool TryGet(
        string token,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        out string correction)
    {
        correction = string.Empty;
        var corrections = language == TypingLanguage.Russian ? Russian : English;
        if (!corrections.TryGetValue(token, out var candidate)
            || !dictionary.Contains(candidate, language))
        {
            return false;
        }

        correction = candidate;
        return true;
    }
}
