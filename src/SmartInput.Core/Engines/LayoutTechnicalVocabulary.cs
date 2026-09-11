using System.Collections.Frozen;

namespace SmartInput.Core.Engines;

/// <summary>
/// Small, bounded vocabulary for short technical layout targets. General
/// words still use the normal frequency and ambiguity gates; short tokens need
/// this extra signal because a three- or four-letter Cyrillic mutation can
/// otherwise have many valid Russian spelling explanations.
/// </summary>
internal static class LayoutTechnicalVocabulary
{
    private static readonly FrozenSet<string> Words = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "api",
        "cli",
        "ui",
        "ux",
        "fast",
        "http",
        "https",
        "json",
        "xml",
        "sql",
        "bash",
        "shell",
        "docker",
        "github",
        "gpu",
        "ram",
        "ssd",
        "cpu",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static bool Contains(string token)
        => Words.Contains(token);
}
