using System.Collections.Concurrent;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Models;
namespace SmartInput.Core.Engines;

/// <summary>
/// A bounded spelling candidate returned by an external local provider.
/// The provider never decides whether a replacement is safe to apply.
/// </summary>
public sealed record ExternalSpellCandidate(
    string Word,
    int EditDistance,
    long Frequency);

public interface IExternalSpellCorrectionProvider
{
    IReadOnlyList<ExternalSpellCandidate> FindCandidates(
        string token,
        TypingLanguage language,
        int maxEditDistance = 1);
}

/// <summary>
/// Combines several local providers without letting either provider decide
/// whether a replacement is safe. Candidates are deduplicated and bounded
/// before the evaluator sees them.
/// </summary>
public sealed class CompositeExternalSpellCorrectionProvider : IExternalSpellCorrectionProvider
{
    private const int MaxReturnedCandidates = 16;
    private readonly IReadOnlyList<IExternalSpellCorrectionProvider> _providers;

    public CompositeExternalSpellCorrectionProvider(
        SymSpellSpellCorrectionProvider symSpell,
        HunspellExternalSpellCorrectionProvider hunspell)
    {
        _providers = new IExternalSpellCorrectionProvider[]
        {
            symSpell ?? throw new ArgumentNullException(nameof(symSpell)),
            hunspell ?? throw new ArgumentNullException(nameof(hunspell)),
        };
    }

    public IReadOnlyList<ExternalSpellCandidate> FindCandidates(
        string token,
        TypingLanguage language,
        int maxEditDistance = 1)
    {
        if (string.IsNullOrWhiteSpace(token) || maxEditDistance < 0)
        {
            return [];
        }

        return _providers
            .SelectMany(provider => provider.FindCandidates(token, language, maxEditDistance))
            .Where(candidate => candidate.EditDistance > 0 && candidate.EditDistance <= maxEditDistance)
            .GroupBy(candidate => AutocorrectDictionaryNormalizer.NormalizeLookupKey(candidate.Word), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => candidate.EditDistance)
                .ThenByDescending(candidate => candidate.Frequency)
                .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(candidate => candidate.EditDistance)
            .ThenByDescending(candidate => candidate.Frequency)
            .ThenBy(candidate => candidate.Word, StringComparer.OrdinalIgnoreCase)
            .Take(MaxReturnedCandidates)
            .ToArray();
    }
}

/// <summary>
/// SymSpell-backed candidate lookup using SmartInput's bundled frequency lexicons.
/// This is an isolated R1 adapter: the live correction pipeline is not wired to it yet.
/// </summary>
public sealed class SymSpellSpellCorrectionProvider : IExternalSpellCorrectionProvider
{
    private const int FrequencyScale = 1_000_000;
    private const int InitialCapacity = 60_000;
    private const int DictionaryMaxEditDistance = 2;
    private const int MaxReturnedCandidates = 16;

    private readonly ConcurrentDictionary<TypingLanguage, Lazy<global::SymSpell>> _instances = new();
    private readonly IHunspellWordFormProvider? _compactWordForms;

    public SymSpellSpellCorrectionProvider()
    {
    }

    public SymSpellSpellCorrectionProvider(IHunspellWordFormProvider compactWordForms)
    {
        _compactWordForms = compactWordForms ?? throw new ArgumentNullException(nameof(compactWordForms));
    }

    /// <summary>
    /// Builds both bounded in-memory indexes before the keyboard hook starts.
    /// Without this call the first live boundary can pay dictionary
    /// construction latency on the input path.
    /// </summary>
    public void WarmUp()
    {
        if (_compactWordForms is not null)
        {
            _compactWordForms.WarmUp();
            return;
        }

        _ = GetInstance(TypingLanguage.English);
        _ = GetInstance(TypingLanguage.Russian);
    }

    public IReadOnlyList<ExternalSpellCandidate> FindCandidates(
        string token,
        TypingLanguage language,
        int maxEditDistance = 1)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (string.IsNullOrWhiteSpace(token)
            || maxEditDistance < 0
            || maxEditDistance > DictionaryMaxEditDistance
            || ProtectedTokenAnalyzer.IsProtected(token)
            || !IsLanguageSupported(token, language))
        {
            return [];
        }

        var normalized = AutocorrectDictionaryNormalizer.NormalizeLookupKey(token);
        if (normalized.Length < 3)
        {
            return [];
        }

        if (_compactWordForms is not null)
        {
            var compactCandidates = _compactWordForms
                .Suggest(normalized, language, maxSuggestions: MaxReturnedCandidates)
                .Select(candidate => new ExternalSpellCandidate(
                    candidate,
                    EditDistance(normalized, candidate),
                    GetBundledFrequency(language, candidate)))
                .Where(candidate => candidate.EditDistance > 0
                    && candidate.EditDistance <= maxEditDistance)
                .OrderBy(candidate => candidate.EditDistance)
                .ThenByDescending(candidate => candidate.Frequency)
                .ThenBy(candidate => candidate.Word, StringComparer.Ordinal)
                .Take(MaxReturnedCandidates)
                .ToArray();

            // A packed one-edit result is the common path. Only pay for the
            // large SymSpell delete index when the compact index cannot
            // produce a candidate and a distance-two search is requested.
            if (compactCandidates.Length > 0 || maxEditDistance < 2)
            {
                return compactCandidates;
            }
        }

        var instance = GetInstance(language);

        var suggestions = instance.Lookup(
            normalized,
            global::SymSpell.Verbosity.Closest,
            maxEditDistance);

        return suggestions
            .Where(suggestion => !string.Equals(
                suggestion.term,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(suggestion => suggestion.distance)
            .ThenByDescending(suggestion => suggestion.count)
            .ThenBy(suggestion => suggestion.term, StringComparer.Ordinal)
            .Take(MaxReturnedCandidates)
            .Select(suggestion => new ExternalSpellCandidate(
                suggestion.term,
                suggestion.distance,
                suggestion.count))
            .ToArray();
    }

    private static long GetBundledFrequency(TypingLanguage language, string word)
    {
        return StarterAutocorrectLexicon.Entries.TryGetValue(
            (language, AutocorrectDictionaryNormalizer.NormalizeLookupKey(word)),
            out var frequency)
            ? (long)Math.Round(frequency * FrequencyScale)
            : 0;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitution = previous[column - 1]
                    + (left[row - 1] == right[column - 1] ? 0 : 1);
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private global::SymSpell GetInstance(TypingLanguage language)
    {
        return _instances
            .GetOrAdd(language, static selectedLanguage =>
                new Lazy<global::SymSpell>(
                    () => BuildInstance(selectedLanguage),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    private static global::SymSpell BuildInstance(TypingLanguage language)
    {
        var instance = new global::SymSpell(InitialCapacity, DictionaryMaxEditDistance);

        foreach (var ((entryLanguage, word), frequency) in StarterAutocorrectLexicon.Entries)
        {
            if (entryLanguage != language)
            {
                continue;
            }

            var normalizedWord = AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
            if (normalizedWord.Length < 3)
            {
                continue;
            }

            var count = Math.Max(1L, (long)Math.Round(frequency * FrequencyScale));
            instance.CreateDictionaryEntry(normalizedWord, count);
        }

        return instance;
    }

    private static bool IsLanguageSupported(string token, TypingLanguage language)
    {
        return language switch
        {
            TypingLanguage.English => TokenScriptAnalyzer.Classify(token) == TokenScript.Latin,
            TypingLanguage.Russian => TokenScriptAnalyzer.Classify(token) == TokenScript.Cyrillic,
            _ => false,
        };
    }
}
