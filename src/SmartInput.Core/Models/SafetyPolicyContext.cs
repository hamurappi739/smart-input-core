using SmartInput.Platform.Abstractions.Security;

namespace SmartInput.Core.Models;

public sealed class SafetyPolicyContext
{
    /// <summary>
    /// Master protection switch. When disabled, no automatic or external text
    /// operation is allowed, including calls that bypass keyboard monitoring.
    /// </summary>
    public bool IsProtectionEnabled { get; init; } = true;

    public bool IsEmergencyPaused { get; init; }

    public SecureInputState SecureInputState { get; init; }

    public bool IsApplicationContextKnown { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string WindowTitle { get; init; } = string.Empty;

    public string WindowClassName { get; init; } = string.Empty;

    public IReadOnlyList<string> ExcludedApplications { get; init; } = [];

    public IReadOnlyList<string> SafeModeApplications { get; init; } = [];

    public IReadOnlyList<string> SafeModeWindowClasses { get; init; } = [];
}
