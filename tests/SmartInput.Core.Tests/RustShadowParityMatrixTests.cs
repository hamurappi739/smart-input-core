using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Infrastructure.Rust;

namespace SmartInput.Core.Tests;

/// <summary>
/// A small, public synthetic matrix for audit-only C#↔Rust comparison. Its
/// output is aggregate-only: it never emits source strings or candidates.
/// </summary>
public sealed class RustShadowParityMatrixTests
{
    [Fact]
    public void SyntheticMatrix_RecordsOnlyAggregateParityCounters()
    {
        var nativePath = Environment.GetEnvironmentVariable(
            RustNativeShadowCandidateProvider.LibraryPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(nativePath))
        {
            return;
        }

        var provider = RustNativeShadowCandidateProvider.CreateFromEnvironment();
        try
        {
            Assert.Equal(RustShadowProviderState.Available, provider.State);

            var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
            var converter = new KeyboardLayoutConverter();
            var joint = new JointCorrectionDecisionService(
                new WrongLayoutDetectionService(converter, dictionary),
                new AutocorrectionService(),
                converter);
            var audit = new RustShadowAuditService(provider);

            foreach (var token in SyntheticTokens)
            {
                var ownDecision = joint.Evaluate(
                    token,
                    dictionary,
                    layoutEnabled: true,
                    autocorrectEnabled: true);
                audit.Observe(token, string.Empty, ownDecision);
            }

            var status = audit.Status;
            Assert.Equal(SyntheticTokens.Length, status.Observed);
            Assert.Equal(0, status.ProviderUnavailable);
            Assert.Equal(0, status.NativeFailures);
            Assert.True(status.TotalEvaluationMicroseconds >= 0);
            Assert.True(status.MaxEvaluationMicroseconds >= 0);

            // The string deliberately contains only aggregate values. It is
            // available under detailed test output for developer comparison.
            Console.WriteLine(
                "RustShadowMatrix "
                + $"observed={status.Observed}; matching={status.MatchingApplies}; "
                + $"differing={status.DifferingApplies}; rust_only={status.RustAppliesOwnKeeps}; "
                + $"csharp_only={status.RustKeepsOwnApplies}; review={status.CandidatesForHumanReview}; "
                + $"avg_us={(status.Observed == 0 ? 0 : status.TotalEvaluationMicroseconds / status.Observed)}; "
                + $"max_us={status.MaxEvaluationMicroseconds}");
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    // Public synthetic regressions only. No user-entered text is included.
    private static readonly string[] SyntheticTokens =
    [
        "ghbdtn",
        "руддщ",
        "стрвнно",
        "кмнда",
        "привет",
        "hello",
        "camelCase",
    ];
}
