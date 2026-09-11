namespace SmartInput.Platform.Abstractions.Application;

public sealed record ActiveApplicationInfo(
    string ProcessName,
    string WindowTitle,
    string WindowClassName,
    nint WindowHandle);

public interface IActiveApplicationService
{
    Task<ActiveApplicationInfo?> GetActiveApplicationAsync(CancellationToken cancellationToken = default);
}
