using SmartInput.App.ViewModels;
using SmartInput.Core.Services;

namespace SmartInput.App.Tests;

public sealed class PunctuationPreviewViewModelTests
{
    [Fact]
    public void Analyze_ShowsLocalQuestionMark_AndAllowsExplicitApply()
    {
        var viewModel = new PunctuationPreviewViewModel(
            new PunctuationPreviewService(new RuleBasedPunctuationProvider()));

        viewModel.SourceText = "как дела";
        viewModel.AnalyzeCommand.Execute(null);

        Assert.True(viewModel.HasPreview);
        Assert.True(viewModel.CanApply);
        Assert.Equal("как дела?", viewModel.PreviewText);

        viewModel.ApplyPreviewCommand.Execute(null);

        Assert.Equal("как дела?", viewModel.SourceText);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void Undo_RestoresTheExactOriginalPreviewText()
    {
        var viewModel = new PunctuationPreviewViewModel(
            new PunctuationPreviewService(new RuleBasedPunctuationProvider()));
        viewModel.SourceText = "это фраза";
        viewModel.AnalyzeCommand.Execute(null);
        viewModel.ApplyPreviewCommand.Execute(null);

        viewModel.UndoPreviewCommand.Execute(null);

        Assert.Equal("это фраза", viewModel.SourceText);
        Assert.Equal("это фраза", viewModel.PreviewText);
        Assert.Contains("отменены", viewModel.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeInput_IsNotPreviewedOrApplied()
    {
        var viewModel = new PunctuationPreviewViewModel(
            new PunctuationPreviewService(new RuleBasedPunctuationProvider()));
        viewModel.SourceText = "hello мир";
        viewModel.AnalyzeCommand.Execute(null);

        Assert.False(viewModel.HasPreview);
        Assert.False(viewModel.CanApply);
        Assert.Equal(string.Empty, viewModel.PreviewText);
    }
}
