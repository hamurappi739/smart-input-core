using System.Collections.Frozen;
using SmartInput.Core.Models;

namespace SmartInput.Core.Dictionaries;

/// <summary>
/// Small deterministic EN/RU n-gram tables for offline prediction in development and tests.
/// </summary>
/// <remarks>
/// Limitations:
/// - Covers only a few dozen bigram/trigram contexts per language.
/// - Not suitable for production-quality prediction coverage.
/// - A future release could load a larger local model file without changing the model contract.
/// </remarks>
public static class StarterPredictionNgrams
{
    public static IReadOnlyDictionary<(TypingLanguage Language, string Word1, string Word2), IReadOnlyList<(string Token, double Score)>> Trigrams { get; } =
        BuildTrigrams();

    public static IReadOnlyDictionary<(TypingLanguage Language, string Word1), IReadOnlyList<(string Token, double Score)>> Bigrams { get; } =
        BuildBigrams();

    public static int EnglishTrigramContextCount { get; }

    public static int RussianTrigramContextCount { get; }

    public static int EnglishBigramContextCount { get; }

    public static int RussianBigramContextCount { get; }

    static StarterPredictionNgrams()
    {
        EnglishTrigramContextCount = Trigrams.Keys.Count(key => key.Language == TypingLanguage.English);
        RussianTrigramContextCount = Trigrams.Keys.Count(key => key.Language == TypingLanguage.Russian);
        EnglishBigramContextCount = Bigrams.Keys.Count(key => key.Language == TypingLanguage.English);
        RussianBigramContextCount = Bigrams.Keys.Count(key => key.Language == TypingLanguage.Russian);
    }

    private static FrozenDictionary<(TypingLanguage, string, string), IReadOnlyList<(string, double)>> BuildTrigrams()
    {
        var entries = new Dictionary<(TypingLanguage, string, string), IReadOnlyList<(string, double)>>();

        AddTrigram(entries, TypingLanguage.English, "how", "are",
        [
            ("you", 0.95),
            ("things", 0.62),
        ]);

        AddTrigram(entries, TypingLanguage.English, "this", "is",
        [
            ("a", 0.85),
            ("the", 0.72),
            ("not", 0.55),
        ]);

        AddTrigram(entries, TypingLanguage.English, "see", "you",
        [
            ("later", 0.80),
            ("soon", 0.55),
        ]);

        AddTrigram(entries, TypingLanguage.English, "good", "morning",
        [
            ("everyone", 0.70),
            ("there", 0.62),
        ]);

        AddTrigram(entries, TypingLanguage.English, "i", "am",
        [
            ("a", 0.74),
            ("the", 0.58),
            ("very", 0.52),
        ]);

        AddTrigram(entries, TypingLanguage.English, "thank", "you",
        [
            ("very", 0.60),
            ("so", 0.52),
        ]);

        AddTrigram(entries, TypingLanguage.Russian, "это", "очень",
        [
            ("хорошо", 0.82),
            ("важно", 0.65),
        ]);

        AddTrigram(entries, TypingLanguage.Russian, "я", "люблю",
        [
            ("тебя", 0.88),
            ("это", 0.55),
        ]);

        AddTrigram(entries, TypingLanguage.Russian, "как", "дела",
        [
            ("у", 0.68),
            ("сегодня", 0.58),
        ]);

        AddTrigram(entries, TypingLanguage.Russian, "доброе", "утро",
        [
            ("всем", 0.72),
            ("друзья", 0.58),
        ]);

        return entries.ToFrozenDictionary();
    }

    private static FrozenDictionary<(TypingLanguage, string), IReadOnlyList<(string, double)>> BuildBigrams()
    {
        var entries = new Dictionary<(TypingLanguage, string), IReadOnlyList<(string, double)>>();

        AddBigram(entries, TypingLanguage.English, "hello",
        [
            ("world", 0.80),
            ("there", 0.75),
        ]);

        AddBigram(entries, TypingLanguage.English, "how",
        [
            ("are", 0.90),
            ("do", 0.62),
        ]);

        AddBigram(entries, TypingLanguage.English, "thank",
        [
            ("you", 0.92),
        ]);

        AddBigram(entries, TypingLanguage.English, "good",
        [
            ("morning", 0.78),
            ("night", 0.70),
        ]);

        AddBigram(entries, TypingLanguage.English, "the",
        [
            ("best", 0.58),
            ("next", 0.52),
        ]);

        AddBigram(entries, TypingLanguage.English, "my",
        [
            ("name", 0.82),
            ("friend", 0.58),
        ]);

        AddBigram(entries, TypingLanguage.Russian, "привет",
        [
            ("мир", 0.85),
            ("друг", 0.72),
        ]);

        AddBigram(entries, TypingLanguage.Russian, "спасибо",
        [
            ("большое", 0.92),
        ]);

        AddBigram(entries, TypingLanguage.Russian, "это",
        [
            ("очень", 0.70),
            ("хорошо", 0.62),
        ]);

        AddBigram(entries, TypingLanguage.Russian, "как",
        [
            ("дела", 0.88),
        ]);

        AddBigram(entries, TypingLanguage.Russian, "очень",
        [
            ("хорошо", 0.76),
            ("важно", 0.58),
        ]);

        AddBigram(entries, TypingLanguage.Russian, "доброе",
        [
            ("утро", 0.84),
        ]);

        return entries.ToFrozenDictionary();
    }

    private static void AddTrigram(
        Dictionary<(TypingLanguage, string, string), IReadOnlyList<(string, double)>> entries,
        TypingLanguage language,
        string word1,
        string word2,
        IEnumerable<(string Token, double Score)> candidates)
    {
        entries[(language, Normalize(word1), Normalize(word2))] = OrderCandidates(candidates);
    }

    private static void AddBigram(
        Dictionary<(TypingLanguage, string), IReadOnlyList<(string, double)>> entries,
        TypingLanguage language,
        string word1,
        IEnumerable<(string Token, double Score)> candidates)
    {
        entries[(language, Normalize(word1))] = OrderCandidates(candidates);
    }

    private static IReadOnlyList<(string Token, double Score)> OrderCandidates(
        IEnumerable<(string Token, double Score)> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Token, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Normalize(string word) =>
        AutocorrectDictionaryNormalizer.NormalizeLookupKey(word);
}
