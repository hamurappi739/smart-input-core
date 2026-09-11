using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class CorrectionRegressionFuzzTests
{
    private const int Seed = 42;
    private const int SampleSize = 5_000;

    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly AutocorrectionService _autocorrectionService = new();
    private readonly JointCorrectionDecisionService _jointService = CreateJointService();

    [Fact]
    public void KnownRussianWords_AreNotConvertedToLatin()
    {
        var words = GetHighFrequencyWords(TypingLanguage.Russian, SampleSize);
        var falsePositives = new List<string>();

        foreach (var word in words)
        {
            var result = _jointService.Evaluate(
                word,
                _dictionary,
                layoutEnabled: true,
                autocorrectEnabled: true);

            if (result.Recommendation == JointCorrectionRecommendation.Apply
                && result.Kind is CorrectionKind.Layout or CorrectionKind.Combined)
            {
                falsePositives.Add(word);
            }
        }

        Assert.Empty(falsePositives);
    }

    [Fact]
    public void KnownEnglishWords_AreNotConvertedToCyrillic()
    {
        var words = GetHighFrequencyWords(TypingLanguage.English, SampleSize);
        var falsePositives = new List<string>();

        foreach (var word in words)
        {
            var result = _jointService.Evaluate(
                word,
                _dictionary,
                layoutEnabled: true,
                autocorrectEnabled: true);

            if (result.Recommendation == JointCorrectionRecommendation.Apply
                && result.Kind is CorrectionKind.Layout or CorrectionKind.Combined)
            {
                falsePositives.Add(word);
            }
        }

        Assert.Empty(falsePositives);
    }

    [Fact]
    public void TrustedRussianWordForms_AreNotReplacedByOtherKnownForms()
    {
        var words = GetHighFrequencyWords(TypingLanguage.Russian, SampleSize);
        var falsePositives = new List<string>();

        foreach (var word in words)
        {
            if (word.Length > 4)
            {
                continue;
            }

            var result = _autocorrectionService.Evaluate(
                word,
                TypingLanguage.Russian,
                _dictionary);

            if (result.Recommendation == AutocorrectionRecommendation.Candidate)
            {
                falsePositives.Add($"{word}->{result.CandidateToken}");
            }
        }

        Assert.Empty(falsePositives);
    }

    [Fact]
    public void SingleRepeatedCharacterTypos_AreCorrectedWhenUnambiguous()
    {
        var random = new Random(Seed);
        var words = GetHighFrequencyWords(TypingLanguage.Russian, 2_000)
            .Where(word => word.Length >= 4)
            .ToList();

        var corrected = 0;
        var ambiguous = 0;

        foreach (var word in words)
        {
            if (!TryCreateSingleRepeatedTypo(word, random, out var typo))
            {
                continue;
            }

            var result = _autocorrectionService.Evaluate(
                typo,
                TypingLanguage.Russian,
                _dictionary);

            if (result.Recommendation == AutocorrectionRecommendation.Candidate)
            {
                if (string.Equals(result.CandidateToken, word, StringComparison.Ordinal))
                {
                    corrected++;
                }
                else
                {
                    ambiguous++;
                }
            }
        }

        Assert.True(corrected > 100);
        Assert.True(ambiguous < corrected / 4);
    }

    [Fact]
    public void CandidateGeneration_CompletesWithinTimeBudget()
    {
        var random = new Random(Seed);
        var words = GetHighFrequencyWords(TypingLanguage.Russian, 200);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var word in words)
        {
            var typo = CreateAdjacentTypo(word, random);
            _ = AutocorrectionCandidateGenerator.Generate(
                typo,
                TypingLanguage.Russian,
                new Configuration.AutocorrectionOptions());
        }

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 2_000);
    }

    [Fact]
    public void JointEvaluation_RespectsCandidateLimits()
    {
        var random = new Random(Seed);
        var words = GetHighFrequencyWords(TypingLanguage.English, 500);
        var options = new Configuration.AutocorrectionOptions
        {
            MaxGeneratedCandidates = 64,
        };

        foreach (var word in words)
        {
            var typo = CreateAdjacentTypo(word, random);
            var candidates = AutocorrectionCandidateGenerator.Generate(
                typo,
                TypingLanguage.English,
                options);

            Assert.True(candidates.Count <= options.MaxGeneratedCandidates);
        }
    }

    private static IReadOnlyList<string> GetHighFrequencyWords(TypingLanguage language, int count)
    {
        return StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == language && pair.Value >= 0.75)
            .Where(pair => pair.Key.Word.Length >= 4)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Word, StringComparer.Ordinal)
            .Take(count)
            .Select(pair => pair.Key.Word)
            .ToList();
    }

    private static bool TryCreateSingleRepeatedTypo(string word, Random random, out string typo)
    {
        typo = string.Empty;
        if (word.Length < 3)
        {
            return false;
        }

        var candidates = new List<int>();
        for (var index = 0; index < word.Length - 1; index++)
        {
            if (word[index] == word[index + 1])
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

    private static string CreateAdjacentTypo(string word, Random random)
    {
        if (word.Length == 0)
        {
            return word;
        }

        var index = random.Next(word.Length);
        var buffer = word.ToCharArray();
        var replacement = (char)('a' + random.Next(26));
        buffer[index] = replacement;
        return new string(buffer);
    }

    private static JointCorrectionDecisionService CreateJointService()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var layoutConverter = new KeyboardLayoutConverter();
        var autocorrectionService = new AutocorrectionService();
        var layoutDetection = new WrongLayoutDetectionService(layoutConverter, dictionary);

        return new JointCorrectionDecisionService(
            layoutDetection,
            autocorrectionService,
            layoutConverter);
    }
}
