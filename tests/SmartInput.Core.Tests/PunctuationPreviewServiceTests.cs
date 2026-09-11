using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public sealed class PunctuationPreviewServiceTests
{
    [Fact]
    public void CreatePreview_InsertsOnlyHighConfidenceMarksWithReversibleEdits()
    {
        var service = new PunctuationPreviewService(new FixedProvider(
            new PunctuationProposal(1, ',', 0.96, "test-v1"),
            new PunctuationProposal(3, '.', 0.99, "test-v1")));

        var result = service.CreatePreview("как у тебя дела");

        Assert.Equal(PunctuationPreviewStatus.PreviewReady, result.Status);
        Assert.Equal("как у, тебя дела.", result.PreviewText);
        Assert.Equal("как у тебя дела", result.OriginalText);
        Assert.Equal("test-v1", result.ProviderVersion);
        Assert.Equal(
            [new PunctuationPreviewEdit(5, ','), new PunctuationPreviewEdit(15, '.')],
            result.Edits);
        Assert.Equal("как у тебя дела", service.RevertPreview(result));
    }

    [Theory]
    [InlineData("https://example.com путь")]
    [InlineData("hello мир")]
    [InlineData("версия 2 тест")]
    [InlineData("hello_world test")]
    [InlineData("текст, уже готов")]
    public void CreatePreview_UnsafeOrExistingPunctuation_DoesNotCallProvider(string text)
    {
        var provider = new CountingProvider();
        var service = new PunctuationPreviewService(provider);

        var result = service.CreatePreview(text);

        Assert.NotEqual(PunctuationPreviewStatus.PreviewReady, result.Status);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public void CreatePreview_SelectsOneBestProposalPerToken_AndIgnoresInvalidValues()
    {
        var service = new PunctuationPreviewService(new FixedProvider(
            new PunctuationProposal(0, ',', 0.91, "v1"),
            new PunctuationProposal(0, '?', 0.93, "v2"),
            new PunctuationProposal(1, '.', 0.50, "v1"),
            new PunctuationProposal(9, '!', 0.99, "v1"),
            new PunctuationProposal(1, '-', 0.99, "v1")));

        var result = service.CreatePreview("как дела");

        Assert.Equal(PunctuationPreviewStatus.PreviewReady, result.Status);
        Assert.Equal("как? дела", result.PreviewText);
        Assert.Single(result.Edits);
    }

    [Fact]
    public void CreatePreview_ProviderFailure_ReturnsNoChangeWithoutInputDetails()
    {
        var service = new PunctuationPreviewService(new ThrowingProvider());

        var result = service.CreatePreview("как дела");

        Assert.Equal(PunctuationPreviewStatus.NoChange, result.Status);
        Assert.Equal(string.Empty, result.PreviewText);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void CreatePreview_OverLimit_IsRejectedBeforeProvider()
    {
        var provider = new CountingProvider();
        var service = new PunctuationPreviewService(provider);

        var result = service.CreatePreview(new string('а', 513));

        Assert.Equal(PunctuationPreviewStatus.InputTooLong, result.Status);
        Assert.Equal(0, provider.Calls);
    }

    private sealed class FixedProvider(params PunctuationProposal[] proposals) : IPunctuationProvider
    {
        public IReadOnlyList<PunctuationProposal> Analyze(string normalizedText) => proposals;
    }

    private sealed class CountingProvider : IPunctuationProvider
    {
        public int Calls { get; private set; }

        public IReadOnlyList<PunctuationProposal> Analyze(string normalizedText)
        {
            Calls++;
            return [];
        }
    }

    private sealed class ThrowingProvider : IPunctuationProvider
    {
        public IReadOnlyList<PunctuationProposal> Analyze(string normalizedText)
            => throw new InvalidOperationException("model failure");
    }
}
