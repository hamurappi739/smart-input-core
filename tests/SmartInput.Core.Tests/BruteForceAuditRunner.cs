using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

internal sealed class AuditCaseSample
{
    public required string Token { get; init; }
    public required TypingLanguage Language { get; init; }
    public string? IntendedSource { get; init; }
    public EditOperationType Operation { get; init; }
    public AuditSampleKind Kind { get; init; }
}

internal enum AuditSampleKind
{
    WrongUniqueTarget,
    OracleProductionDisagreement,
    AmbiguousApplied,
    WrongDirectLayout,
    WrongCombined,
    MandatoryRegression,
    ShortToken,
    YeYo,
}

internal static class BruteForceAuditRunner
{
    internal static void RunExpandedVerification(
        MaximumCorpusAuditReport report,
        IReadOnlyList<AuditCaseSample> samples,
        IAutocorrectDictionary dictionary,
        CandidateAmbiguityIndex index,
        AutocorrectionOptions options)
    {
        foreach (var sample in samples)
        {
            report.BruteForceCasesEvaluated++;
            var brute = string.IsNullOrEmpty(sample.IntendedSource)
                ? BruteForceMutationVerifier.Analyze(sample.Token, sample.Language, dictionary, options)
                : BruteForceMutationVerifier.AnalyzeGeneratedCase(
                    sample.Token,
                    sample.IntendedSource,
                    sample.Language,
                    dictionary,
                    options);
            var independent = string.IsNullOrEmpty(sample.IntendedSource)
                ? MutationClassificationOracle.Analyze(sample.Token, sample.Language, dictionary, options)
                : IndependentCorpusVerificationOracle.AnalyzeGeneratedCase(
                    sample.Token,
                    sample.IntendedSource,
                    sample.Language,
                    dictionary,
                    options);

            if (BruteForceMatchesOracle(brute, independent))
            {
                report.BruteForceOracleAgreements++;
            }
            else
            {
                report.BruteForceOracleDisagreements++;
                var disagreement = BruteForceOracleDisagreementClassifier.Classify(
                    sample.Token,
                    sample.Language,
                    brute,
                    independent);
                BruteForceOracleDisagreementClassifier.Record(report, disagreement);
                if (report.BruteForceFailureSamples.Count < 40)
                {
                    report.BruteForceFailureSamples.Add(
                        AuditCaseHasher.HashCase(
                            $"bf≠oracle:{sample.Language}:{disagreement.PrimaryReason}",
                            brute.Class.ToString(),
                            independent.Class.ToString()));
                }
            }

            var productionAllows = ProductionCandidateSafetyClassifier.AllowsSpellingApply(
                sample.Token,
                independent.UniqueTarget ?? sample.IntendedSource ?? string.Empty,
                sample.Language,
                dictionary,
                options,
                index);

            var bruteSaysUnique = brute.Class == MutationOracleClass.UniquelyRecoverable;
            if (bruteSaysUnique == productionAllows)
            {
                report.BruteForceProductionAgreements++;
            }
            else
            {
                report.BruteForceProductionDisagreements++;
            }
        }
    }

    private static bool BruteForceMatchesOracle(
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
}

internal static class SignatureIndexCompletenessAuditor
{
    internal static void AuditSingleCase(
        MaximumCorpusAuditReport report,
        string token,
        TypingLanguage language,
        string intended,
        MutationAnalysisResult oracle,
        CandidateAmbiguityIndex index)
    {
        _ = intended;
        if (oracle.Sources.Count == 0)
        {
            return;
        }

        var lookupWatch = System.Diagnostics.Stopwatch.StartNew();
        var discovered = index.FindComparableKnownTargets(token, language, bestEditCost: 2.0);
        lookupWatch.Stop();
        report.IndexLookupCount++;
        report.IndexCompetitorsReturnedTotal += discovered.Count;
        report.IndexMaxCompetitorsReturned = Math.Max(report.IndexMaxCompetitorsReturned, discovered.Count);
        report.IndexLookupDurationsMs.Add(lookupWatch.Elapsed.TotalMilliseconds);
        report.IndexMemoryEstimateBytes = index.EstimatedBytes;

        var minimumCost = oracle.Sources.Min(static source => source.EditCost);
        foreach (var source in oracle.Sources.Where(source => source.EditCost <= minimumCost + 0.08))
        {
            if (discovered.Any(neighbor =>
                    string.Equals(neighbor.Word, source.Word, StringComparison.OrdinalIgnoreCase))
                || index.CanDiscoverCompetitor(token, language, source.Word, source.EditCost))
            {
                continue;
            }

            report.ProductionSignatureIndexMiss++;
            var kind = MapOperationToIndexKind(source.Operation);
            report.IndexMissesBySignature.TryGetValue(kind, out var count);
            report.IndexMissesBySignature[kind] = count + 1;
            if (report.IndexMissSamples.Count < 40)
            {
                report.IndexMissSamples.Add(
                    AuditCaseHasher.HashCase(
                        $"indexMiss:{language}:{source.Operation}",
                        source.Word.Length.ToString(),
                        kind.ToString()));
            }
        }
    }

    private static ProductionSignatureIndexKind MapOperationToIndexKind(EditOperationType operation)
        => operation switch
        {
            EditOperationType.RepeatedAccidentalCharacter => ProductionSignatureIndexKind.RepeatedCharacter,
            EditOperationType.ExtraCharacter => ProductionSignatureIndexKind.Insertion,
            EditOperationType.MissingCharacter => ProductionSignatureIndexKind.Deletion,
            EditOperationType.AdjacentTransposition => ProductionSignatureIndexKind.AdjacentTransposition,
            EditOperationType.AdjacentKeySubstitution => ProductionSignatureIndexKind.AdjacentKeySubstitution,
            EditOperationType.VowelSubstitution => ProductionSignatureIndexKind.VowelSubstitution,
            EditOperationType.GeneralSubstitution => ProductionSignatureIndexKind.GeneralSubstitution,
            EditOperationType.DirectLayout => ProductionSignatureIndexKind.DirectPhysicalLayout,
            EditOperationType.CombinedLayoutSpelling => ProductionSignatureIndexKind.CombinedLayoutSpelling,
            _ => ProductionSignatureIndexKind.GeneralSubstitution,
        };
}

internal static class OperationDisagreementCrossTable
{
    internal static void Record(
        MaximumCorpusAuditReport report,
        EditOperationType operation,
        OracleProductionDisagreementReason reason)
    {
        var key = (operation, reason);
        report.OperationDisagreementCounts.TryGetValue(key, out var count);
        report.OperationDisagreementCounts[key] = count + 1;
    }

    internal static void AssertReconciles(MaximumCorpusAuditReport report)
    {
        CorpusAuditAccounting.SyncCanonicalCounters(report);
        var sum = report.OperationDisagreementCounts.Values.Sum();
        if (sum != report.OracleProductionDisagreementCount)
        {
            throw new InvalidOperationException(
                $"Sum(primary disagreement buckets)={sum} != OracleProductionDisagreement={report.OracleProductionDisagreementCount}");
        }
    }

    internal static string Format(MaximumCorpusAuditReport report)
    {
        var lines = report.OperationDisagreementCounts
            .OrderBy(static pair => pair.Key.Operation)
            .ThenBy(static pair => pair.Key.Reason)
            .Select(pair => $"{pair.Key.Operation}:{pair.Key.Reason}={pair.Value}");
        return string.Join(",", lines);
    }
}

internal sealed class StratifiedAuditSampler
{
    private readonly Random _random;
    private readonly Dictionary<string, int> _buckets = new(StringComparer.Ordinal);
    private readonly List<AuditCaseSample> _samples = [];
    private readonly int _targetDisagreements;
    private readonly int _targetUnique;
    private readonly int _targetAmbiguous;

    internal StratifiedAuditSampler(
        int seed,
        int targetDisagreements = 5_000,
        int targetUnique = 5_000,
        int targetAmbiguous = 5_000)
    {
        _random = new Random(seed);
        _targetDisagreements = targetDisagreements;
        _targetUnique = targetUnique;
        _targetAmbiguous = targetAmbiguous;
    }

    internal IReadOnlyList<AuditCaseSample> Samples => _samples;

    internal void Consider(AuditCaseSample sample)
    {
        if (sample.Kind == AuditSampleKind.OracleProductionDisagreement
            && _samples.Count(s => s.Kind == AuditSampleKind.OracleProductionDisagreement) >= _targetDisagreements)
        {
            return;
        }

        if (sample.Kind is AuditSampleKind.ShortToken or AuditSampleKind.YeYo
            && _samples.Count(s => s.Kind == sample.Kind) >= 250)
        {
            return;
        }

        var bucket = $"{sample.Kind}:{sample.Language}:{sample.Operation}:{sample.Token.Length switch
        {
            <= 3 => "short",
            <= 7 => "medium",
            _ => "long",
        }}";
        _buckets.TryGetValue(bucket, out var seen);
        if (sample.Kind == AuditSampleKind.OracleProductionDisagreement && seen >= 8)
        {
            return;
        }

        if (sample.Kind is AuditSampleKind.ShortToken or AuditSampleKind.YeYo && seen >= 8)
        {
            return;
        }

        _buckets[bucket] = seen + 1;
        _samples.Add(sample);
    }

    internal void ConsiderUniqueOrAmbiguous(AuditCaseSample sample, MutationOracleClass oracleClass)
    {
        if (oracleClass == MutationOracleClass.UniquelyRecoverable
            && sample.Kind != AuditSampleKind.WrongUniqueTarget
            && _samples.Count(s => s.Kind != AuditSampleKind.OracleProductionDisagreement) >= _targetUnique
            && _random.NextDouble() > 0.05)
        {
            return;
        }

        if (oracleClass == MutationOracleClass.Ambiguous
            && sample.Kind != AuditSampleKind.OracleProductionDisagreement
            && _samples.Count(s => s.Kind == AuditSampleKind.OracleProductionDisagreement) >= _targetAmbiguous
            && _random.NextDouble() > 0.05)
        {
            return;
        }

        Consider(sample);
    }
}
