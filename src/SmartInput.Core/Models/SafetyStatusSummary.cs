namespace SmartInput.Core.Models;

public enum SafetyStatusLevel
{
    Normal,
    Caution,
    Restricted,
    Inactive,
    Paused,
}

public sealed class SafetyStatusSummary
{
    public required string Headline { get; init; }

    public required string Detail { get; init; }

    public SafetyStatusLevel Level { get; init; }

    public bool IsAutomationAllowed { get; init; }

    public AutomationPolicyState PolicyState { get; init; }

    public bool IsProtectionEnabled { get; init; }

    public bool IsEmergencyPauseEnabled { get; init; }

    public bool IsMonitoringActive { get; init; }
}
