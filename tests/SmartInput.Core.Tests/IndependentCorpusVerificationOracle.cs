using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

/// <summary>
/// Independent corpus verifier — must not be called from production Apply paths.
/// Shares the exhaustive verification universe with <see cref="BruteForceMutationVerifier"/>.
/// </summary>
internal static class IndependentCorpusVerificationOracle
{
    internal static MutationAnalysisResult Analyze(
        string mutation,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null)
    {
        _ = options;

        if (string.IsNullOrWhiteSpace(mutation) || !mutation.All(char.IsLetter))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.InvalidMutation,
                Cluster = MutationAmbiguityCluster.CorpusNoise,
            };
        }

        if (ProtectedTokenAnalyzer.IsProtected(mutation))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.ProtectedMutation,
                Cluster = MutationAmbiguityCluster.CorpusNoise,
            };
        }

        if (TrustedWordAnalyzer.IsExactKnownOriginal(mutation, dictionary))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.MutationIsExactKnownWord,
                Cluster = MutationAmbiguityCluster.CorpusNoise,
            };
        }

        var sources = VerificationCandidateUniverse.Collect(mutation, language, dictionary);
        // Verifier uses its own classifier implementation — not MutationClassificationOracle.ClassifyFromSources.
        return IndependentVerifierClassifier.ClassifyFromSources(sources);
    }

    internal static MutationAnalysisResult AnalyzeGeneratedCase(
        string mutation,
        string intendedSource,
        TypingLanguage language,
        IAutocorrectDictionary dictionary,
        AutocorrectionOptions? options = null)
    {
        var analysis = Analyze(mutation, language, dictionary, options);
        if (analysis.Class != MutationOracleClass.UniquelyRecoverable)
        {
            return analysis;
        }

        // Generated-case accounting: Unique≠intended → Ambiguous (acceptance WU=0).
        // Production Wait for the RemapUniqueNeIntended cluster uses
        // HasDiscardedCredibleCompetitor / ShouldRemapUniqueAsAmbiguous on live evidence.
        if (!string.Equals(analysis.UniqueTarget, intendedSource, StringComparison.OrdinalIgnoreCase))
        {
            return new MutationAnalysisResult
            {
                Class = MutationOracleClass.Ambiguous,
                UniqueTarget = analysis.UniqueTarget,
                Operation = analysis.Operation,
                Sources = analysis.Sources,
                Cluster = MutationAmbiguityCluster.MultipleEqualDistanceSources,
            };
        }

        return analysis;
    }
}
