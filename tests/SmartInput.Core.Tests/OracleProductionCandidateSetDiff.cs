using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Test-only production-vs-verifier candidate-set comparison for OracleProductionDisagreement cases.
/// </summary>
internal static class OracleProductionCandidateSetDiff
{
    internal sealed record CandidateSetDiffResult(
        OracleProductionDisagreementReason PrimaryReason,
        int OracleTargetsMissingFromProduction,
        int ProductionTargetsMissingFromOracle,
        int TargetsPresentWithDifferentOperation,
        int TargetsPresentWithDifferentCost,
        int TargetsPresentWithDifferentTier,
        int TargetsPresentWithDifferentFrequencyBand,
        int LayoutAlternativeMissing,
        int CombinedAlternativeMissing,
        string OracleDescription,
        string ProductionDescription);

    internal static CandidateSetDiffResult Analyze(
        string mutation,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options,
        MutationAnalysisResult independentOracle,
        JointCorrectionDecisionResult productionResult)
    {
        var productionOracle = MutationClassificationOracle.Analyze(
            mutation,
            language,
            dictionary,
            options);

        var oracleBand = BuildMinimumBand(independentOracle.Sources);
        var productionBand = BuildMinimumBand(productionOracle.Sources);

        var oracleMap = oracleBand.ToDictionary(
            static source => source.Word,
            static source => source,
            StringComparer.OrdinalIgnoreCase);
        var productionMap = productionBand.ToDictionary(
            static source => source.Word,
            static source => source,
            StringComparer.OrdinalIgnoreCase);

        var oracleMissing = oracleMap.Keys
            .Count(word => !productionMap.ContainsKey(word));
        var productionMissing = productionMap.Keys
            .Count(word => !oracleMap.ContainsKey(word));

        var differentOperation = 0;
        var differentCost = 0;
        var differentTier = 0;
        var differentFrequencyBand = 0;

        foreach (var word in oracleMap.Keys.Intersect(productionMap.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var oracleSource = oracleMap[word];
            var productionSource = productionMap[word];
            if (oracleSource.Operation != productionSource.Operation)
            {
                differentOperation++;
            }

            if (Math.Abs(oracleSource.EditCost - productionSource.EditCost) > 0.01)
            {
                differentCost++;
            }

            if (oracleSource.DictionaryTier != productionSource.DictionaryTier)
            {
                differentTier++;
            }

            if (GetFrequencyBand(oracleSource.Frequency) != GetFrequencyBand(productionSource.Frequency))
            {
                differentFrequencyBand++;
            }
        }

        var layoutMissing = productionResult.Kind == CorrectionKind.Layout ? 0 : CountLayoutAlternatives(mutation, language, dictionary);
        var combinedMissing = productionResult.Kind == CorrectionKind.Combined ? 0 : CountCombinedAlternatives(mutation, language, dictionary, options);

        var reason = ClassifyReason(
            independentOracle,
            productionOracle,
            oracleMissing,
            productionMissing,
            differentOperation,
            differentCost,
            differentTier,
            layoutMissing,
            combinedMissing);

        return new CandidateSetDiffResult(
            reason,
            oracleMissing,
            productionMissing,
            differentOperation,
            differentCost,
            differentTier,
            differentFrequencyBand,
            layoutMissing > 0 ? 1 : 0,
            combinedMissing > 0 ? 1 : 0,
            FormatOracleDescription(mutation, independentOracle, oracleBand),
            FormatProductionDescription(productionOracle, productionBand, productionResult));
    }

    internal static void RecordDiff(
        MaximumCorpusAuditReport report,
        CandidateSetDiffResult diff,
        EditOperationType operation)
    {
        CorpusAuditAccounting.RecordDisagreementReason(report, diff.PrimaryReason);
        OperationDisagreementCrossTable.Record(report, operation, diff.PrimaryReason);
        report.OracleTargetsMissingFromProduction += diff.OracleTargetsMissingFromProduction;
        report.ProductionTargetsMissingFromOracle += diff.ProductionTargetsMissingFromOracle;
        report.TargetsPresentWithDifferentOperation += diff.TargetsPresentWithDifferentOperation;
        report.TargetsPresentWithDifferentCost += diff.TargetsPresentWithDifferentCost;
        report.TargetsPresentWithDifferentTier += diff.TargetsPresentWithDifferentTier;
        report.TargetsPresentWithDifferentFrequencyBand += diff.TargetsPresentWithDifferentFrequencyBand;
        report.LayoutAlternativeMissing += diff.LayoutAlternativeMissing;
        report.CombinedAlternativeMissing += diff.CombinedAlternativeMissing;
    }

    private static List<PossibleMutationSource> BuildMinimumBand(IReadOnlyList<PossibleMutationSource> sources)
    {
        if (sources.Count == 0)
        {
            return [];
        }

        var minimumCost = sources.Min(static source => source.EditCost);
        return sources
            .Where(source => source.EditCost <= minimumCost + 0.08)
            .OrderBy(static source => source.EditCost)
            .ThenByDescending(static source => source.Frequency)
            .ThenBy(static source => source.Word, StringComparer.Ordinal)
            .ToList();
    }

    private static OracleProductionDisagreementReason ClassifyReason(
        MutationAnalysisResult independentOracle,
        MutationAnalysisResult productionOracle,
        int oracleMissing,
        int productionMissing,
        int differentOperation,
        int differentCost,
        int differentTier,
        int layoutAlternatives,
        int combinedAlternatives)
    {
        if (oracleMissing > 0)
        {
            return OracleProductionDisagreementReason.ProductionSignatureIndexMiss;
        }

        if (productionMissing > 0)
        {
            return OracleProductionDisagreementReason.CandidateGenerationTruncation;
        }

        if (differentOperation > 0)
        {
            return OracleProductionDisagreementReason.OperationDefinitionMismatch;
        }

        if (differentCost > 0)
        {
            return OracleProductionDisagreementReason.EditCostMismatch;
        }

        if (differentTier > 0)
        {
            return OracleProductionDisagreementReason.DictionaryTierMismatch;
        }

        if (independentOracle.Class == MutationOracleClass.Ambiguous
            && productionOracle.Class == MutationOracleClass.UniquelyRecoverable)
        {
            if (layoutAlternatives > 0)
            {
                return OracleProductionDisagreementReason.DirectLayoutCompetitorMissing;
            }

            if (combinedAlternatives > 0)
            {
                return OracleProductionDisagreementReason.CombinedCompetitorMissing;
            }

            return OracleProductionDisagreementReason.CrossOperationCompetitorMissing;
        }

        return OracleProductionDisagreementReason.Unclassified;
    }

    private static int CountLayoutAlternatives(
        string mutation,
        TypingLanguage language,
        IAutocorrectDictionary dictionary)
    {
        var converter = new KeyboardLayoutConverter();
        var direction = language == TypingLanguage.Russian
            ? LayoutConversionDirection.RussianToEnglish
            : LayoutConversionDirection.EnglishToRussian;
        var mapped = converter.Convert(mutation, direction);
        if (string.Equals(mapped, mutation, StringComparison.Ordinal))
        {
            return 0;
        }

        var targetLanguage = language == TypingLanguage.Russian
            ? TypingLanguage.English
            : TypingLanguage.Russian;
        return dictionary.Contains(mapped, targetLanguage) ? 1 : 0;
    }

    private static int CountCombinedAlternatives(
        string mutation,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions options)
    {
        var converter = new KeyboardLayoutConverter();
        var direction = language == TypingLanguage.Russian
            ? LayoutConversionDirection.RussianToEnglish
            : LayoutConversionDirection.EnglishToRussian;
        var mapped = converter.Convert(mutation, direction);
        if (string.Equals(mapped, mutation, StringComparison.Ordinal))
        {
            return 0;
        }

        var targetLanguage = language == TypingLanguage.Russian
            ? TypingLanguage.English
            : TypingLanguage.Russian;
        var spelling = MutationClassificationOracle.Analyze(mapped, targetLanguage, dictionary, options);
        return spelling.Class == MutationOracleClass.UniquelyRecoverable ? 1 : 0;
    }

    private static string GetFrequencyBand(double frequency)
    {
        if (frequency >= 0.995)
        {
            return "ultra";
        }

        if (frequency >= 0.90)
        {
            return "high";
        }

        if (frequency >= 0.70)
        {
            return "medium";
        }

        return "low";
    }

    private static string FormatOracleDescription(
        string mutation,
        MutationAnalysisResult oracle,
        IReadOnlyList<PossibleMutationSource> band)
    {
        var targets = string.Join(
            ";",
            band.Select(source =>
                $"{source.Word}:{source.Operation}:{source.EditCost:F2}:{GetFrequencyBand(source.Frequency)}"));
        return
            $"token={mutation}; class={oracle.Class}; cluster={oracle.Cluster}; "
            + $"operation={oracle.Operation}; targets=[{targets}]";
    }

    private static string FormatProductionDescription(
        MutationAnalysisResult production,
        IReadOnlyList<PossibleMutationSource> band,
        JointCorrectionDecisionResult result)
    {
        var targets = string.Join(
            ";",
            band.Select(source =>
                $"{source.Word}:{source.Operation}:{source.EditCost:F2}:{GetFrequencyBand(source.Frequency)}"));
        return
            $"class={production.Class}; operation={production.Operation}; targets=[{targets}]; "
            + $"final={result.ReplacementToken}/{result.Kind}";
    }
}
