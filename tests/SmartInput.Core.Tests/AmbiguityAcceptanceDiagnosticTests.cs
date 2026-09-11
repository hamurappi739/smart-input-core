using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Privacy-safe diagnostic sampler for remaining corpus acceptance blockers.
/// Emits hashed caseIds only — never raw tokens.
/// </summary>
[Trait("Category", "FocusedCorrection")]
public class AmbiguityAcceptanceDiagnosticTests
{
    private const double SafetyRecoveryFloor = 0.90;

    [Fact]
    public void Probe_MandatorySpellingParents_RemainUnique()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var options = new AutocorrectionOptions();

        foreach (var (token, language, expected) in new (string, TypingLanguage, string)[]
                 {
                     ("helo", TypingLanguage.English, "hello"),
                     ("teh", TypingLanguage.English, "the"),
                     ("adn", TypingLanguage.English, "and"),
                     ("миняй", TypingLanguage.Russian, "меняй"),
                     ("превет", TypingLanguage.Russian, "привет"),
                 })
        {
            var analysis = MutationClassificationOracle.Analyze(token, language, dictionary, options);
            Assert.Equal(MutationOracleClass.UniquelyRecoverable, analysis.Class);
            Assert.Equal(expected, analysis.UniqueTarget);
        }
    }

    [Fact]
    public void Write_PrivacySafeDiagnosticReport_ForSeed42Sample()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon(converter)),
            converter);
        var options = new AutocorrectionOptions();

        var report = MaximumCorpusAuditHarness.RunFullAudit(
            seed: 42,
            minRussianMutations: 12_000,
            minEnglishMutations: 12_000);

        var sb = new StringBuilder();
        sb.AppendLine("hashedCaseId|operation|productionCandidateKind|candidateEvidence|independentClassification|decision|firstDivergenceStage|reason");

        var ambiguous = 0;
        var wrongUnique = 0;
        var wrongLayout = 0;
        var clusters = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var sample in report.AuditSamples)
        {
            if (sample.Kind is not (AuditSampleKind.AmbiguousApplied
                or AuditSampleKind.WrongUniqueTarget
                or AuditSampleKind.WrongDirectLayout))
            {
                continue;
            }

            if (sample.Kind == AuditSampleKind.AmbiguousApplied && ambiguous >= 100)
            {
                continue;
            }

            if (sample.Kind == AuditSampleKind.WrongUniqueTarget && wrongUnique >= 500)
            {
                continue;
            }

            if (sample.Kind == AuditSampleKind.WrongDirectLayout && wrongLayout >= 500)
            {
                continue;
            }

            var production = MutationClassificationOracle.Analyze(
                sample.Token,
                sample.Language,
                dictionary,
                options);
            var independent = IndependentCorpusVerificationOracle.Analyze(
                sample.Token,
                sample.Language,
                dictionary,
                options);
            var intended = sample.IntendedSource ?? string.Empty;
            var generated = IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
                sample.Token,
                intended,
                sample.Language,
                dictionary,
                options);
            var decision = joint.Evaluate(sample.Token, dictionary, true, true);
            var caseId = AuditCaseHasher.HashCase(
                sample.Token,
                intended,
                decision.ReplacementToken);

            Assert.DoesNotContain(sample.Token, caseId, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(intended))
            {
                Assert.DoesNotContain(intended, caseId, StringComparison.OrdinalIgnoreCase);
            }

            var intendedInSources = production.Sources.Any(source =>
                string.Equals(source.Word, intended, StringComparison.OrdinalIgnoreCase));
            var uniqueEqIntended = string.Equals(
                production.UniqueTarget,
                intended,
                StringComparison.OrdinalIgnoreCase);
            var uniqueEqReplacement = string.Equals(
                production.UniqueTarget,
                decision.ReplacementToken,
                StringComparison.OrdinalIgnoreCase);
            var sourceDistinct = production.Sources
                .Select(source => source.Word)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var reason =
                generated.Class == MutationOracleClass.Ambiguous
                && independent.Class == MutationOracleClass.UniquelyRecoverable
                    ? "RemapUniqueNeIntended"
                    : independent.Class == MutationOracleClass.Ambiguous
                        ? "IndependentAmbiguous"
                        : !uniqueEqIntended && independent.Class == MutationOracleClass.UniquelyRecoverable
                            ? "UniqueWrongTarget"
                            : "Other";

            var divergence =
                decision.Kind == CorrectionKind.Layout
                && production.Class == MutationOracleClass.UniquelyRecoverable
                && !uniqueEqReplacement
                    ? "LayoutOverUniqueSpelling"
                    : decision.Kind == CorrectionKind.Layout
                        ? "DirectLayoutGate"
                        : reason == "RemapUniqueNeIntended"
                            ? "GeneratedRemap"
                            : production.Class == MutationOracleClass.UniquelyRecoverable
                                ? "SpellingUniqueApply"
                                : "SpellingNonUniqueApply";

            var evidence =
                $"src={sourceDistinct};intendedIn={intendedInSources};uniqEqInt={uniqueEqIntended};uniqEqRep={uniqueEqReplacement};prod={production.Class};ind={independent.Class};gen={generated.Class};discarded={OperationPrecisionGate.HasDiscardedCredibleCompetitor(production)}";

            sb.Append(caseId).Append('|')
                .Append(sample.Operation).Append('|')
                .Append(decision.Kind).Append('|')
                .Append(evidence).Append('|')
                .Append(independent.Class).Append('|')
                .Append(decision.Recommendation).Append('|')
                .Append(divergence).Append('|')
                .Append(reason)
                .AppendLine();

            var cluster =
                $"{sample.Kind}|{reason}|{decision.Kind}|{divergence}|prod={production.Class}|ind={independent.Class}";
            clusters.TryGetValue(cluster, out var count);
            clusters[cluster] = count + 1;

            switch (sample.Kind)
            {
                case AuditSampleKind.AmbiguousApplied:
                    ambiguous++;
                    break;
                case AuditSampleKind.WrongUniqueTarget:
                    wrongUnique++;
                    break;
                case AuditSampleKind.WrongDirectLayout:
                    wrongLayout++;
                    break;
            }
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            "smartinput-ambiguity-diagnostic-seed42.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        var clusterSummary = string.Join(
            " || ",
            clusters.OrderByDescending(pair => pair.Value).Take(25)
                .Select(pair => $"{pair.Value}x {pair.Key}"));

        Console.WriteLine($"diagnosticReport={path}");
        Console.WriteLine($"sampled AA={ambiguous} WU={wrongUnique} WL={wrongLayout}");
        Console.WriteLine($"BFOracleDisagreement={report.BruteForceOracleDisagreements}");
        Console.WriteLine($"ProductionSignatureIndexMiss={report.ProductionSignatureIndexMiss}");
        Console.WriteLine($"TotalAmbiguousApplied={report.TotalAmbiguousApplied}");
        Console.WriteLine($"WrongUniqueTarget={report.WrongUniqueTarget}");
        Console.WriteLine($"WrongDirectLayout={report.WrongDirectLayout}");
        Console.WriteLine($"WrongCombined={report.WrongCombined}");
        Console.WriteLine($"ExactWordsChanged={report.ExactWordsChanged}");
        Console.WriteLine($"unique recovery={report.RecoveryRate:P2}");
        Console.WriteLine($"PeakWorkingSetBytes={report.PeakWorkingSetBytes}");
        Console.WriteLine($"clusters={clusterSummary}");

        Assert.True(
            report.TotalMutations >= 20_000
                && report.ExactWordsChanged == 0,
            $"BFOracleDisagreement={report.BruteForceOracleDisagreements}; "
            + $"ProductionSignatureIndexMiss={report.ProductionSignatureIndexMiss}; "
            + $"TotalAmbiguousApplied={report.TotalAmbiguousApplied}; "
            + $"WrongUniqueTarget={report.WrongUniqueTarget}; "
            + $"WrongDirectLayout={report.WrongDirectLayout}; "
            + $"WrongDirectLayoutMutation={report.WrongDirectLayoutMutation}; "
            + $"WrongDirectLayoutPairHarness={report.WrongDirectLayoutPairHarness}; "
            + $"AmbiguousDirectLayoutApply={report.AmbiguousDirectLayoutApply}; "
            + $"MustPreserveApplied={report.MustPreserveApplied}; "
            + $"MustWaitApplied={report.MustWaitApplied}; "
            + $"WrongCombined={report.WrongCombined}; "
            + $"ExactWordsChanged={report.ExactWordsChanged}; "
            + $"unique recovery={report.RecoveryRate:P2}; "
            + $"PeakWorkingSetBytes={report.PeakWorkingSetBytes}; "
            + $"layoutPairDiagnostics={report.LayoutPairDiagnostics.Count}; "
            + $"clusters={clusterSummary}; report={path}");

        // Acceptance is not complete while any required counter is non-zero.
        Assert.True(
            report.BruteForceOracleDisagreements == 0
            && report.ProductionSignatureIndexMiss == 0
            && report.TotalAmbiguousApplied == 0
            && report.WrongUniqueTarget == 0
            && report.WrongDirectLayout == 0
            && report.WrongCombined == 0
            && report.ExactWordsChanged == 0
            && report.RecoveryRate >= SafetyRecoveryFloor,
            $"BFOracleDisagreement={report.BruteForceOracleDisagreements}; "
            + $"ProductionSignatureIndexMiss={report.ProductionSignatureIndexMiss}; "
            + $"TotalAmbiguousApplied={report.TotalAmbiguousApplied}; "
            + $"WrongUniqueTarget={report.WrongUniqueTarget}; "
            + $"WrongDirectLayout={report.WrongDirectLayout}; "
            + $"WrongDirectLayoutMutation={report.WrongDirectLayoutMutation}; "
            + $"WrongDirectLayoutPairHarness={report.WrongDirectLayoutPairHarness}; "
            + $"AmbiguousDirectLayoutApply={report.AmbiguousDirectLayoutApply}; "
            + $"MustPreserveApplied={report.MustPreserveApplied}; "
            + $"MustWaitApplied={report.MustWaitApplied}; "
            + $"WrongCombined={report.WrongCombined}; "
            + $"ExactWordsChanged={report.ExactWordsChanged}; "
            + $"unique recovery={report.RecoveryRate:P2}; "
            + $"PeakWorkingSetBytes={report.PeakWorkingSetBytes}; "
            + $"clusters={clusterSummary}");
    }
}
