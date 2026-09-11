namespace SmartInput.Platform.Abstractions.Text;

public interface ITextReplacementService
{
    Task<TextReplacementResult> ReplaceRecentTextAsync(
        TextReplacementRequest request,
        CancellationToken cancellationToken = default);
}
