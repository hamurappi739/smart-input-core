using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

internal enum BruteForceOracleDisagreementReason
{
    None,
    GeneralSubstitutionNotEnumerated,
    VowelSubstitutionNotEnumerated,
    AdjacentKeyDirectionMismatch,
    InsertionDirectionMismatch,
    DeletionDirectionMismatch,
    TranspositionMismatch,
    RepeatedRunMismatch,
    YeYoNormalizationMismatch,
    CaseNormalizationMismatch,
    LanguageFilterMismatch,
    TokenLengthEligibilityMismatch,
    DictionaryMembershipMismatch,
    FrequencyBandMismatch,
    EditCostMismatch,
    ProtectedTokenMismatch,
    DuplicateCandidateMismatch,
    ClassMismatch,
    UniqueTargetMismatch,
    Unclassified,
}

internal sealed record BruteForceOracleDisagreement(
    string Token,
    TypingLanguage Language,
    BruteForceOracleDisagreementReason PrimaryReason,
    MutationOracleClass BruteClass,
    MutationOracleClass OracleClass,
    string? BruteTarget,
    string? OracleTarget,
    IReadOnlyList<string> SourcesMissingFromOracle,
    IReadOnlyList<string> SourcesOnlyInOracle);

/// <summary>
/// Classifies BF vs independent-oracle disagreements into exactly one primary bucket.
/// </summary>
internal static class BruteForceOracleDisagreementClassifier
{
    internal static BruteForceOracleDisagreement Classify(
        string token,
        TypingLanguage language,
        BruteForceMutationVerifier.BruteForceResult brute,
        MutationAnalysisResult oracle)
    {
        var bruteSources = brute.CredibleSources
            .Select(source => source.Word)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oracleSources = oracle.Sources
            .Select(source => source.Word)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingFromOracle = bruteSources.Except(oracleSources, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyInOracle = oracleSources.Except(bruteSources, StringComparer.OrdinalIgnoreCase).ToList();

        var reason = ResolvePrimaryReason(token, language, brute, oracle, missingFromOracle, onlyInOracle);
        return new BruteForceOracleDisagreement(
            token,
            language,
            reason,
            brute.Class,
            oracle.Class,
            brute.UniqueTarget,
            oracle.UniqueTarget,
            missingFromOracle,
            onlyInOracle);
    }

    internal static void Record(
        MaximumCorpusAuditReport report,
        BruteForceOracleDisagreement disagreement)
    {
        report.BruteForceOracleDisagreementReasons.TryGetValue(disagreement.PrimaryReason, out var count);
        report.BruteForceOracleDisagreementReasons[disagreement.PrimaryReason] = count + 1;
    }

    internal static void AssertReconciles(MaximumCorpusAuditReport report)
    {
        var sum = report.BruteForceOracleDisagreementReasons.Values.Sum();
        if (sum != report.BruteForceOracleDisagreements)
        {
            throw new InvalidOperationException(
                $"Sum(primary BF/oracle disagreement buckets)={sum} != BFOracleDisagreement={report.BruteForceOracleDisagreements}");
        }
    }

    private static BruteForceOracleDisagreementReason ResolvePrimaryReason(
        string token,
        TypingLanguage language,
        BruteForceMutationVerifier.BruteForceResult brute,
        MutationAnalysisResult oracle,
        List<string> missingFromOracle,
        List<string> onlyInOracle)
    {
        if (brute.Class is MutationOracleClass.ProtectedMutation or MutationOracleClass.InvalidMutation
            || oracle.Class is MutationOracleClass.ProtectedMutation or MutationOracleClass.InvalidMutation)
        {
            return BruteForceOracleDisagreementReason.ProtectedTokenMismatch;
        }

        if (brute.Class == MutationOracleClass.MutationIsExactKnownWord
            || oracle.Class == MutationOracleClass.MutationIsExactKnownWord)
        {
            return BruteForceOracleDisagreementReason.DictionaryMembershipMismatch;
        }

        if (missingFromOracle.Count > 0 || onlyInOracle.Count > 0)
        {
            var probe = missingFromOracle.Concat(onlyInOracle).First();
            var operation = VerificationEditProvenance.Classify(token.ToLowerInvariant(), probe.ToLowerInvariant(), language);
            return operation switch
            {
                EditOperationType.GeneralSubstitution => BruteForceOracleDisagreementReason.GeneralSubstitutionNotEnumerated,
                EditOperationType.VowelSubstitution => BruteForceOracleDisagreementReason.VowelSubstitutionNotEnumerated,
                EditOperationType.AdjacentKeySubstitution => BruteForceOracleDisagreementReason.AdjacentKeyDirectionMismatch,
                EditOperationType.ExtraCharacter => BruteForceOracleDisagreementReason.InsertionDirectionMismatch,
                EditOperationType.MissingCharacter => BruteForceOracleDisagreementReason.DeletionDirectionMismatch,
                EditOperationType.AdjacentTransposition => BruteForceOracleDisagreementReason.TranspositionMismatch,
                EditOperationType.RepeatedAccidentalCharacter => BruteForceOracleDisagreementReason.RepeatedRunMismatch,
                _ => token.Contains('е') || token.Contains('ё') || probe.Contains('е') || probe.Contains('ё')
                    ? BruteForceOracleDisagreementReason.YeYoNormalizationMismatch
                    : BruteForceOracleDisagreementReason.DictionaryMembershipMismatch,
            };
        }

        if (brute.Class != oracle.Class)
        {
            return BruteForceOracleDisagreementReason.ClassMismatch;
        }

        if (brute.Class == MutationOracleClass.UniquelyRecoverable
            && !string.Equals(brute.UniqueTarget, oracle.UniqueTarget, StringComparison.OrdinalIgnoreCase))
        {
            return BruteForceOracleDisagreementReason.UniqueTargetMismatch;
        }

        var bruteByWord = brute.CredibleSources.ToDictionary(s => s.Word, StringComparer.OrdinalIgnoreCase);
        foreach (var source in oracle.Sources)
        {
            if (!bruteByWord.TryGetValue(source.Word, out var bruteSource))
            {
                continue;
            }

            if (Math.Abs(bruteSource.EditCost - source.EditCost) > 0.01)
            {
                return BruteForceOracleDisagreementReason.EditCostMismatch;
            }

            if (GetFrequencyBand(bruteSource.Frequency) != GetFrequencyBand(source.Frequency))
            {
                return BruteForceOracleDisagreementReason.FrequencyBandMismatch;
            }

            if (bruteSource.Operation != source.Operation)
            {
                return bruteSource.Operation switch
                {
                    EditOperationType.AdjacentKeySubstitution or EditOperationType.GeneralSubstitution
                        or EditOperationType.VowelSubstitution
                        => BruteForceOracleDisagreementReason.AdjacentKeyDirectionMismatch,
                    EditOperationType.RepeatedAccidentalCharacter
                        => BruteForceOracleDisagreementReason.RepeatedRunMismatch,
                    _ => BruteForceOracleDisagreementReason.DuplicateCandidateMismatch,
                };
            }
        }

        return BruteForceOracleDisagreementReason.Unclassified;
    }

    private static string GetFrequencyBand(double frequency)
        => frequency switch
        {
            >= 0.995 => "ultra",
            >= 0.90 => "high",
            >= 0.70 => "medium",
            _ => "low",
        };
}
