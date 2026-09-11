using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Slow brute-force verifier for bounded diagnostic slices. Test-only.
/// Reimplements classification via the shared exhaustive universe; must agree with
/// <see cref="IndependentCorpusVerificationOracle"/> without sharing its Analyze method.
/// </summary>
internal static class BruteForceMutationVerifier
{
    internal sealed record BruteForceResult(
        MutationOracleClass Class,
        string? UniqueTarget,
        EditOperationType Operation,
        IReadOnlyList<PossibleMutationSource> CredibleSources);

    internal static BruteForceResult Analyze(
        string mutation,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null)
    {
        _ = options;

        if (string.IsNullOrWhiteSpace(mutation) || !mutation.All(char.IsLetter))
        {
            return new BruteForceResult(MutationOracleClass.InvalidMutation, null, EditOperationType.Unknown, []);
        }

        if (ProtectedTokenAnalyzer.IsProtected(mutation))
        {
            return new BruteForceResult(MutationOracleClass.ProtectedMutation, null, EditOperationType.Unknown, []);
        }

        if (TrustedWordAnalyzer.IsExactKnownOriginal(mutation, dictionary))
        {
            return new BruteForceResult(MutationOracleClass.MutationIsExactKnownWord, null, EditOperationType.Unknown, []);
        }

        var sources = VerificationCandidateUniverse.Collect(mutation, language, dictionary);
        var classified = IndependentVerifierClassifier.ClassifyFromSources(sources);
        return new BruteForceResult(
            classified.Class,
            classified.UniqueTarget,
            classified.Operation,
            classified.Sources);
    }

    internal static BruteForceResult AnalyzeGeneratedCase(
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null)
    {
        var result = Analyze(mutation, language, dictionary, options);
        if (result.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return result;
        }

        // Generated-case accounting: Unique≠intended → Ambiguous (aligned with
        // IndependentCorpusVerificationOracle / ReverseMutationIndex).
        if (!string.Equals(result.UniqueTarget, intendedSource, StringComparison.OrdinalIgnoreCase))
        {
            return new BruteForceResult(
                MutationOracleClass.Ambiguous,
                result.UniqueTarget,
                result.Operation,
                result.CredibleSources);
        }

        return result;
    }
}
