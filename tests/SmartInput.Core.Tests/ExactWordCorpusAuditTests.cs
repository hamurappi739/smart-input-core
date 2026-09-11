using System.Diagnostics;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class ExactWordCorpusAuditTests
{
    private const int CollisionSampleSeed = 42;

    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly JointCorrectionDecisionService _joint;
    private readonly AutocorrectionService _autocorrection = new();

    public ExactWordCorpusAuditTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            _autocorrection,
            converter);
    }

    [Fact]
    public void FullRussianLexicon_ExactWords_HaveZeroFalseCorrections()
    {
        var words = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian)
            .Select(pair => pair.Key.Word)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(words.Count >= 40_000);

        var falsePositives = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        foreach (var word in words)
        {
            var result = _joint.Evaluate(word, _dictionary, layoutEnabled: true, autocorrectEnabled: true);
            if (result.Recommendation == JointCorrectionRecommendation.Apply)
            {
                falsePositives.Add($"{word}->{result.ReplacementToken}/{result.Kind}");
                if (falsePositives.Count >= 25)
                {
                    break;
                }
            }
        }

        stopwatch.Stop();

        Assert.True(
            falsePositives.Count == 0,
            $"RU exact-word false corrections: {falsePositives.Count}; "
            + $"checked={words.Count}; elapsedMs={stopwatch.ElapsedMilliseconds}; "
            + $"samples={string.Join(", ", falsePositives.Take(10))}");
    }

    [Fact]
    public void FullEnglishLexicon_ExactWords_HaveZeroFalseCorrections()
    {
        var words = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.English)
            .Select(pair => pair.Key.Word)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(words.Count >= 40_000);

        var falsePositives = new List<string>();
        var checkedCount = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var word in words)
        {
            // Intentional short wrong-layout whitelist is an explicit exception.
            if (LayoutServiceWordWhitelist.TryGetRussianReplacement(word, out _))
            {
                continue;
            }

            checkedCount++;
            var result = _joint.Evaluate(word, _dictionary, layoutEnabled: true, autocorrectEnabled: true);
            if (result.Recommendation == JointCorrectionRecommendation.Apply)
            {
                falsePositives.Add($"{word}->{result.ReplacementToken}/{result.Kind}");
                if (falsePositives.Count >= 25)
                {
                    break;
                }
            }
        }

        stopwatch.Stop();

        Assert.True(
            falsePositives.Count == 0,
            $"EN exact-word false corrections: {falsePositives.Count}; "
            + $"checked={checkedCount}; elapsedMs={stopwatch.ElapsedMilliseconds}; "
            + $"samples={string.Join(", ", falsePositives.Take(10))}");
    }

    [Fact]
    public void KnownToKnownCollisionPairs_HaveZeroAutomaticSubstitutions()
    {
        var russianWords = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian)
            .Select(pair => pair.Key.Word)
            .Where(word => word.Length is >= 2 and <= 8)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToList();

        var wordSet = russianWords.ToHashSet(StringComparer.Ordinal);
        var pairsChecked = 0;
        var falsePositives = new List<string>();
        var random = new Random(CollisionSampleSeed);

        // Deterministic sample of words; for each, probe bounded one-edit neighbors.
        var sample = russianWords
            .Where((_, index) => index % 7 == 0)
            .Take(8_000)
            .ToList();

        foreach (var word in sample)
        {
            foreach (var neighbor in GenerateOneEditNeighbors(word, random))
            {
                if (!wordSet.Contains(neighbor) || string.Equals(word, neighbor, StringComparison.Ordinal))
                {
                    continue;
                }

                pairsChecked++;
                var result = _joint.Evaluate(word, _dictionary, true, true);
                if (result.Recommendation == JointCorrectionRecommendation.Apply
                    && string.Equals(result.ReplacementToken, neighbor, StringComparison.OrdinalIgnoreCase))
                {
                    falsePositives.Add($"{word}->{neighbor}");
                    if (falsePositives.Count >= 20)
                    {
                        break;
                    }
                }
            }

            if (falsePositives.Count >= 20)
            {
                break;
            }
        }

        Assert.True(pairsChecked > 500, $"Expected many known-to-known pairs, got {pairsChecked}");
        Assert.True(
            falsePositives.Count == 0,
            $"known-to-known false substitutions={falsePositives.Count}; pairsChecked={pairsChecked}; "
            + $"samples={string.Join(", ", falsePositives.Take(10))}");
    }

    [Fact]
    public void JointDecision_PerformanceBudget_ForMixedCorpus()
    {
        var words = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian)
            .Select(pair => pair.Key.Word)
            .Where(word => word.Length is >= 3 and <= 12)
            .Take(2_000)
            .Concat(
                StarterAutocorrectLexicon.Entries
                    .Where(pair => pair.Key.Language == TypingLanguage.English)
                    .Select(pair => pair.Key.Word)
                    .Where(word => word.Length is >= 3 and <= 12)
                    .Take(2_000))
            .ToList();

        // Warm ambiguity index / joint path before measuring.
        _ = new AutocorrectionService();
        _ = _joint.Evaluate("warmup", _dictionary, true, true);

        var durations = new List<double>(words.Count);
        var stopwatch = Stopwatch.StartNew();

        foreach (var word in words)
        {
            var local = Stopwatch.StartNew();
            _ = _joint.Evaluate(word, _dictionary, true, true);
            local.Stop();
            durations.Add(local.Elapsed.TotalMilliseconds);
        }

        stopwatch.Stop();
        durations.Sort();

        var average = durations.Average();
        var p95 = durations[(int)(durations.Count * 0.95)];
        var max = durations[^1];

        Assert.True(average < 5.0, $"Average joint decision too slow: {average:F3} ms");
        Assert.True(p95 < 20.0, $"p95 joint decision too slow: {p95:F3} ms");
        Assert.True(max < 75.0, $"Max joint decision too slow: {max:F3} ms");
        Assert.True(stopwatch.ElapsedMilliseconds < 20_000);
    }

    [Fact]
    public void RepeatedCharacterMutations_DoNotInventWrongKnownWords()
    {
        var random = new Random(CollisionSampleSeed);
        var words = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian && pair.Value >= 0.85)
            .Select(pair => pair.Key.Word)
            .Where(word => word.Length is >= 4 and <= 10)
            .Take(1_500)
            .ToList();

        var wrongConfident = 0;
        var corrected = 0;
        var wrongSamples = new List<string>();

        foreach (var word in words)
        {
            if (!TryCreateSingleRepeatedTypo(word, random, out var typo))
            {
                continue;
            }

            // Skip mutations that accidentally create another exact dictionary word.
            if (TrustedWordAnalyzer.IsExactKnownOriginal(typo, _dictionary))
            {
                continue;
            }

            var result = _joint.Evaluate(typo, _dictionary, true, true);
            if (result.Recommendation != JointCorrectionRecommendation.Apply
                || string.IsNullOrEmpty(result.ReplacementToken))
            {
                continue;
            }

            if (string.Equals(result.ReplacementToken, word, StringComparison.Ordinal))
            {
                corrected++;
            }
            else if (TrustedWordAnalyzer.IsExactKnownOriginal(result.ReplacementToken, _dictionary)
                     && TrustedWordAnalyzer.IsExactKnownOriginal(word, _dictionary))
            {
                wrongConfident++;
                if (wrongSamples.Count < 8)
                {
                    wrongSamples.Add($"{typo}->{result.ReplacementToken} (from {word})");
                }
            }
        }

        Assert.True(corrected > 50);
        Assert.True(
            wrongConfident == 0,
            $"wrongConfident={wrongConfident}; samples={string.Join("; ", wrongSamples)}");
    }

    private static IEnumerable<string> GenerateOneEditNeighbors(string word, Random random)
    {
        if (word.Length == 0)
        {
            yield break;
        }

        // Substitutions at a few deterministic positions.
        var positions = new[] { 0, word.Length / 2, word.Length - 1 }.Distinct();
        foreach (var index in positions)
        {
            var original = word[index];
            foreach (var replacement in GetNearbyRussianLetters(original, random))
            {
                var buffer = word.ToCharArray();
                buffer[index] = replacement;
                yield return new string(buffer);
            }
        }

        // Single deletion
        if (word.Length >= 3)
        {
            yield return word.Remove(word.Length / 2, 1);
        }

        // Single repeated insertion
        yield return word.Insert(Math.Min(1, word.Length), word[0].ToString());

        // Adjacent transposition
        if (word.Length >= 2)
        {
            var buffer = word.ToCharArray();
            (buffer[0], buffer[1]) = (buffer[1], buffer[0]);
            yield return new string(buffer);
        }
    }

    private static IEnumerable<char> GetNearbyRussianLetters(char original, Random random)
    {
        const string alphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        yield return alphabet[random.Next(alphabet.Length)];
        if (original is >= 'а' and <= 'я')
        {
            yield return original == 'а' ? 'о' : 'а';
            yield return original == 'и' ? 'е' : 'и';
        }
    }

    private static bool TryCreateSingleRepeatedTypo(string word, Random random, out string typo)
    {
        typo = string.Empty;
        var candidates = new List<int>();
        for (var index = 0; index < word.Length; index++)
        {
            if (index + 1 < word.Length && word[index] == word[index + 1])
            {
                continue;
            }

            candidates.Add(index);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var position = candidates[random.Next(candidates.Count)];
        typo = word.Insert(position, word[position].ToString());
        return true;
    }
}
