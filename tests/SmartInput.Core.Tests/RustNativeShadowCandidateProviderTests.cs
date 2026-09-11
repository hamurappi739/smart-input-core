using SmartInput.Core.Integration;
using SmartInput.Infrastructure.Rust;

namespace SmartInput.Core.Tests;

public sealed class RustNativeShadowCandidateProviderTests
{
    [Fact]
    public void CreateFromEnvironment_WithoutExplicitPath_RemainsDisabled()
    {
        var nativePath = Environment.GetEnvironmentVariable(
            RustNativeShadowCandidateProvider.LibraryPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(nativePath))
        {
            return;
        }

        var provider = RustNativeShadowCandidateProvider.CreateFromEnvironment();
        var result = provider.Evaluate("token", "token ");

        Assert.Equal(RustShadowProviderState.Disabled, provider.State);
        Assert.Equal(RustShadowProviderState.Disabled, result.ProviderState);
    }

    [Fact]
    public void CreateFromEnvironment_WhenNativePathIsProvided_UsesAuditOnlyCandidate()
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

            AssertReplace(provider.Evaluate("ghbdtn", "ghbdtn "), "привет", RustShadowReason.Layout);
            AssertReplace(provider.Evaluate("руддщ", "руддщ "), "hello", RustShadowReason.Layout);
            AssertReplace(provider.Evaluate("стрвнно", "стрвнно "), "странно", RustShadowReason.Spelling);
            AssertReplace(provider.Evaluate("кмнда", "кмнда "), "команда", RustShadowReason.Spelling);

            AssertKeep(provider.Evaluate("hello", "hello "));
            AssertKeep(provider.Evaluate("ghbdtn", "https://ghbdtn.example"));
            AssertKeep(provider.Evaluate("myVariable", "myVariable "));
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    private static void AssertReplace(
        RustShadowCandidateResult result,
        string expectedReplacement,
        RustShadowReason expectedReason)
    {
        Assert.Equal(RustShadowProviderState.Available, result.ProviderState);
        Assert.Equal(RustShadowDecision.Replace, result.Decision);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(expectedReplacement, result.ReplacementToken);
    }

    private static void AssertKeep(RustShadowCandidateResult result)
    {
        Assert.Equal(RustShadowProviderState.Available, result.ProviderState);
        Assert.Equal(RustShadowDecision.Keep, result.Decision);
        Assert.Null(result.ReplacementToken);
    }
}
