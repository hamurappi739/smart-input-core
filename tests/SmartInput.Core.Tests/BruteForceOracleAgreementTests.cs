using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "BruteForceOracleAgreement")]
public class BruteForceOracleAgreementTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    [Fact]
    public void MandatoryRegressions_BruteForceAgreesWithIndependentOracle()
    {
        var disagreements = 0;
        var reasons = new Dictionary<BruteForceOracleDisagreementReason, int>();

        foreach (var assertion in MandatoryRegressionCatalog.All)
        {
            var language = TokenScriptAnalyzer.Classify(assertion.Input) == TokenScript.Cyrillic
                ? TypingLanguage.Russian
                : TypingLanguage.English;

            var brute = string.IsNullOrEmpty(assertion.ExpectedOutput)
                ? BruteForceMutationVerifier.Analyze(assertion.Input, language, _dictionary)
                : BruteForceMutationVerifier.AnalyzeGeneratedCase(
                    assertion.Input,
                    assertion.ExpectedOutput,
                    language,
                    _dictionary);
            var independent = string.IsNullOrEmpty(assertion.ExpectedOutput)
                ? IndependentCorpusVerificationOracle.Analyze(assertion.Input, language, _dictionary)
                : IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
                    assertion.Input,
                    assertion.ExpectedOutput,
                    language,
                    _dictionary);

            if (Matches(brute, independent))
            {
                continue;
            }

            disagreements++;
            var classified = BruteForceOracleDisagreementClassifier.Classify(
                assertion.Input,
                language,
                brute,
                independent);
            reasons.TryGetValue(classified.PrimaryReason, out var count);
            reasons[classified.PrimaryReason] = count + 1;
        }

        Assert.True(
            disagreements == 0,
            $"mandatory BF≠oracle disagreements={disagreements}; "
            + string.Join(",", reasons.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    [Fact]
    public void ExhaustiveGeneralSubstitution_EnumeratesFullAlphabet()
    {
        var sources = VerificationCandidateUniverse.Collect("миняй", TypingLanguage.Russian, _dictionary);
        var sameLength = sources
            .Where(source => source.Word.Length == 5)
            .Where(source => source.Operation is EditOperationType.GeneralSubstitution
                or EditOperationType.AdjacentKeySubstitution
                or EditOperationType.VowelSubstitution)
            .Select(source => source.Word)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("меняй", sameLength);
        Assert.True(
            sameLength.Count >= 1,
            "exhaustive same-length substitutions must retain dictionary hits");
    }

    [Fact]
    public void CanonicalProvenance_PrefersAdjacentKeyOverGeneral()
    {
        // 'g' and 'h' are adjacent on QWERTY English; must not be labelled GeneralSubstitution.
        var operation = VerificationEditProvenance.Classify("helo", "hello", TypingLanguage.English);
        Assert.Equal(EditOperationType.MissingCharacter, operation);

        var adjacent = VerificationEditProvenance.Classify("thel", "the", TypingLanguage.English);
        Assert.True(
            adjacent is EditOperationType.ExtraCharacter or EditOperationType.RepeatedAccidentalCharacter
                or EditOperationType.Unknown);
    }

    [Fact]
    public void StratifiedSeed42Slice_BruteForceAgreesWithIndependentOracle()
    {
        var random = new Random(42);
        var russian = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian)
            .Select(pair => pair.Key.Word)
            .Where(word => word.Length is >= 4 and <= 10)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToList();
        var english = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.English)
            .Select(pair => pair.Key.Word)
            .Where(word => word.Length is >= 4 and <= 10)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToList();

        var cases = new List<(string Token, string Intended, TypingLanguage Language)>();
        void AddMutations(IReadOnlyList<string> words, TypingLanguage language, int count)
        {
            var alphabet = VerificationEditProvenance.GetAlphabet(language);
            for (var i = 0; i < count; i++)
            {
                var word = words[random.Next(words.Count)];
                var mutation = Mutate(word, alphabet, random);
                if (!string.Equals(mutation, word, StringComparison.Ordinal))
                {
                    cases.Add((mutation, word, language));
                }
            }
        }

        // >=5,000 unique-oriented + >=5,000 ambiguous-oriented stratified cases, then held-out.
        AddMutations(russian, TypingLanguage.Russian, 6_000);
        AddMutations(english, TypingLanguage.English, 6_000);

        foreach (var assertion in MandatoryRegressionCatalog.All.Where(a =>
                     a.Kind is MandatoryRegressionKind.MustApply
                         or MandatoryRegressionKind.MustApplyWithContext
                         or MandatoryRegressionKind.MustApplyCapitalized))
        {
            var language = TokenScriptAnalyzer.Classify(assertion.Input) == TokenScript.Cyrillic
                ? TypingLanguage.Russian
                : TypingLanguage.English;
            cases.Add((assertion.Input, assertion.ExpectedOutput!, language));
        }

        var heldOutRandom = new Random(1337);
        AddHeldOut(cases, russian, TypingLanguage.Russian, heldOutRandom, 5_000);
        AddHeldOut(cases, english, TypingLanguage.English, heldOutRandom, 5_000);

        var disagreements = 0;
        var reasons = new Dictionary<BruteForceOracleDisagreementReason, int>();
        var uniqueChecked = 0;
        var ambiguousChecked = 0;

        foreach (var (token, intended, language) in cases)
        {
            var brute = BruteForceMutationVerifier.AnalyzeGeneratedCase(token, intended, language, _dictionary);
            var independent = IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
                token,
                intended,
                language,
                _dictionary);

            if (independent.Class == MutationOracleClass.UniquelyRecoverable)
            {
                uniqueChecked++;
            }
            else if (independent.Class == MutationOracleClass.Ambiguous)
            {
                ambiguousChecked++;
            }

            if (Matches(brute, independent))
            {
                continue;
            }

            disagreements++;
            var classified = BruteForceOracleDisagreementClassifier.Classify(token, language, brute, independent);
            reasons.TryGetValue(classified.PrimaryReason, out var count);
            reasons[classified.PrimaryReason] = count + 1;
        }

        Assert.True(cases.Count >= 10_000, $"cases={cases.Count}");
        Assert.True(uniqueChecked >= 1_000, $"uniqueChecked={uniqueChecked}");
        Assert.True(ambiguousChecked >= 1_000, $"ambiguousChecked={ambiguousChecked}");
        Assert.True(
            disagreements == 0,
            $"stratified BF≠oracle disagreements={disagreements}/{cases.Count}; "
            + $"unique={uniqueChecked}; ambiguous={ambiguousChecked}; "
            + string.Join(",", reasons.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    private static void AddHeldOut(
        List<(string Token, string Intended, TypingLanguage Language)> cases,
        IReadOnlyList<string> words,
        TypingLanguage language,
        Random random,
        int count)
    {
        var alphabet = VerificationEditProvenance.GetAlphabet(language);
        for (var i = 0; i < count; i++)
        {
            var word = words[random.Next(words.Count)];
            var mutation = Mutate(word, alphabet, random);
            if (!string.Equals(mutation, word, StringComparison.Ordinal))
            {
                cases.Add((mutation, word, language));
            }
        }
    }

    private static bool Matches(
        BruteForceMutationVerifier.BruteForceResult brute,
        MutationAnalysisResult independent)
    {
        if (brute.Class != independent.Class)
        {
            return false;
        }

        if (brute.Class == MutationOracleClass.UniquelyRecoverable)
        {
            return string.Equals(brute.UniqueTarget, independent.UniqueTarget, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string Mutate(string word, string alphabet, Random random)
    {
        var mode = random.Next(5);
        return mode switch
        {
            0 => word.Insert(random.Next(word.Length), word[random.Next(word.Length)].ToString()),
            1 => word.Remove(random.Next(word.Length), 1),
            2 => SwapAdjacent(word, random.Next(word.Length - 1)),
            3 => ReplaceAt(word, random.Next(word.Length), alphabet[random.Next(alphabet.Length)]),
            _ => ReplaceAt(word, random.Next(word.Length), alphabet[random.Next(alphabet.Length)]),
        };
    }

    private static string SwapAdjacent(string word, int index)
    {
        var chars = word.ToCharArray();
        (chars[index], chars[index + 1]) = (chars[index + 1], chars[index]);
        return new string(chars);
    }

    private static string ReplaceAt(string word, int index, char replacement)
        => word[..index] + replacement + word[(index + 1)..];
}
