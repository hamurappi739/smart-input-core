namespace SmartInput.Platform.Abstractions.Text;

public interface ISelectedTextService
{
    Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default);

    Task<bool> ReplaceSelectedTextAsync(string replacementText, CancellationToken cancellationToken = default);
}
