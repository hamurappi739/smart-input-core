using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

[Trait("Category", "SignatureIndexCompleteness")]
public class SignatureIndexCompletenessTests
{
    [Fact]
    public void WildcardIndex_DiscoversGeneralSubstitutionCompetitor()
    {
        var index = CandidateAmbiguityIndex.ForStarterLexicon(new KeyboardLayoutConverter());
        Assert.True(index.CanDiscoverCompetitor("миняй", TypingLanguage.Russian, "меняй", 1.0));
        Assert.True(index.EstimatedBytes > 0);
    }

    [Fact]
    public void StratifiedMutations_MinimumBandFullyIndexed()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var index = CandidateAmbiguityIndex.ForStarterLexicon(new KeyboardLayoutConverter());
        var random = new Random(42);
        var words = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian)
            .Select(pair => pair.Key.Word)
            .Where(word => word.Length is >= 4 and <= 8)
            .OrderBy(word => word, StringComparer.Ordinal)
            .ToList();

        var report = new MaximumCorpusAuditReport { Seed = 42 };
        var alphabet = VerificationEditProvenance.GetAlphabet(TypingLanguage.Russian);
        for (var i = 0; i < 2_000; i++)
        {
            var word = words[random.Next(words.Count)];
            var mutation = word.ToCharArray();
            mutation[random.Next(mutation.Length)] = alphabet[random.Next(alphabet.Length)];
            var token = new string(mutation);
            if (string.Equals(token, word, StringComparison.Ordinal))
            {
                continue;
            }

            var oracle = IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
                token,
                word,
                TypingLanguage.Russian,
                dictionary);
            SignatureIndexCompletenessAuditor.AuditSingleCase(
                report,
                token,
                TypingLanguage.Russian,
                word,
                oracle,
                index);
        }

        Assert.True(
            report.ProductionSignatureIndexMiss == 0,
            $"indexMiss={report.ProductionSignatureIndexMiss}; samples={string.Join(" | ", report.IndexMissSamples.Take(10))}");
    }
}
