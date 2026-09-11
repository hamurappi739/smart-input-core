using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public class SafetyStatusFormatterTests
{
    [Fact]
    public void Create_WhenEmergencyPauseEnabled_ReturnsPausedSummary()
    {
        var settings = new AppSettings { IsEnabled = true, EmergencyPauseEnabled = true };
        var policy = AutomationPolicyResult.EmergencyPaused();

        var summary = SafetyStatusFormatter.Create(settings, policy, isMonitoring: true);

        Assert.Equal(SafetyStatusLevel.Paused, summary.Level);
        Assert.False(summary.IsAutomationAllowed);
        Assert.True(summary.IsEmergencyPauseEnabled);
    }

    [Fact]
    public void Create_WhenProtectionDisabled_ReturnsInactiveSummary()
    {
        var settings = new AppSettings { IsEnabled = false };
        var policy = new AutomationPolicyResult
        {
            State = AutomationPolicyState.Allowed,
            AllowsAutomation = true,
            AllowsManualExternalTextOperations = true,
        };

        var summary = SafetyStatusFormatter.Create(settings, policy, isMonitoring: false);

        Assert.Equal(SafetyStatusLevel.Inactive, summary.Level);
        Assert.False(summary.IsMonitoringActive);
    }

    [Fact]
    public void Create_WhenSafeModeActive_ReturnsCautionSummary()
    {
        var settings = new AppSettings { IsEnabled = true };
        var policy = new AutomationPolicyResult
        {
            State = AutomationPolicyState.SafeMode,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = true,
            Reason = "Developer tool detected.",
        };

        var summary = SafetyStatusFormatter.Create(settings, policy, isMonitoring: true);

        Assert.Equal(SafetyStatusLevel.Caution, summary.Level);
        Assert.Equal(AutomationPolicyState.SafeMode, summary.PolicyState);
    }
}
