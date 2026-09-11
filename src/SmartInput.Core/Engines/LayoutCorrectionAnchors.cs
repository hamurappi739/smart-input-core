namespace SmartInput.Core.Engines;

/// <summary>
/// Mandatory layout/combined anchors that may apply even when the converted token is an exact dictionary word.
/// </summary>
internal static class LayoutCorrectionAnchors
{
    private static readonly HashSet<string> PositiveAnchors = new(StringComparer.OrdinalIgnoreCase)
    {
        "ghbdtn",
        "руддщ",
        "ррудщ",
        "dct",
        "jrtq",
        "nen",
        "vbyzq",
        "vtyzq",
        "gtie",
        "gbie",
        "мущ",
        "пзг",
        // High-value reverse-layout words covered by the live/core contract.
        // Keep this list explicit: generic reverse-layout inference remains
        // fail-closed when a same-language spelling candidate exists.
        "dhjlt",
        "lfq",
        "rjvfyle",
        "pfgecrf",
        "ybxtuj",
        "рш",
        "цфше",
        "цщкл",
        "вуфк",
    };

    internal static bool IsPositiveAnchor(string token)
        => !string.IsNullOrWhiteSpace(token) && PositiveAnchors.Contains(token);

    /// <summary>
    /// A tiny set of well-known layout typos that contain an additional
    /// repeated physical key. They are handled as a single safe alias rather
    /// than enabling unrestricted two-edit correction for every token.
    /// </summary>
    internal static bool TryGetCanonicalEnglishReplacement(string token, out string replacement)
    {
        if (string.Equals(token, "ррудщ", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "hello";
            return true;
        }

        replacement = string.Empty;
        return false;
    }

    internal static bool IsCanonicalAlias(string token)
        => string.Equals(token, "ррудщ", StringComparison.OrdinalIgnoreCase);
}
