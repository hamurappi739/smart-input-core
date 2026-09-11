using SmartInput.Core.Models;

namespace SmartInput.Core.Engines;

/// <summary>
/// Finds bundled or user-installed Hunspell language packs without downloading
/// anything at runtime and without making the application depend on a network
/// service. Bundled packs are checked first, then the per-user directory.
/// </summary>
public static class HunspellDictionaryCatalog
{
    public static IReadOnlyDictionary<TypingLanguage, HunspellDictionaryFiles> Discover(
        string? explicitRoot = null)
    {
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            // An explicit root is an isolated test/deployment location. Do
            // not silently merge machine-wide dictionaries into it.
            roots.Add(explicitRoot);
        }
        else
        {
            roots.Add(Path.Combine(AppContext.BaseDirectory, "Dictionaries", "Hunspell"));

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                roots.Add(Path.Combine(localAppData, "SmartInput", "Dictionaries"));
            }
        }

        var result = new Dictionary<TypingLanguage, HunspellDictionaryFiles>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddIfPresent(result, root, TypingLanguage.English, "en_US");
            AddIfPresent(result, root, TypingLanguage.Russian, "ru_RU");
        }

        return result;
    }

    private static void AddIfPresent(
        IDictionary<TypingLanguage, HunspellDictionaryFiles> result,
        string root,
        TypingLanguage language,
        string stem)
    {
        var dictionaryPath = Path.Combine(root, stem + ".dic");
        var affixPath = Path.Combine(root, stem + ".aff");
        if (File.Exists(dictionaryPath) && File.Exists(affixPath))
        {
            var packedPath = Path.Combine(root, stem + ".sidict");
            result.TryAdd(
                language,
                new HunspellDictionaryFiles(
                    dictionaryPath,
                    affixPath,
                    File.Exists(packedPath) ? packedPath : null));
        }
    }
}
