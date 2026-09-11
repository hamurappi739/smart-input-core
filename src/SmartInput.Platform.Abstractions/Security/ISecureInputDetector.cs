namespace SmartInput.Platform.Abstractions.Security;

public enum SecureInputState
{
    Inactive,
    Active,
    Unknown,
}

public sealed record SecureInputDetectionResult(SecureInputState State);

public interface ISecureInputDetector
{
    Task<SecureInputDetectionResult> DetectAsync(CancellationToken cancellationToken = default);
}
