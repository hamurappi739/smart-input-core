using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Security;

namespace SmartInput.Core.Configuration;

public static class SafetyPolicyDefaults
{
    public static SafetyPolicyContext CreateDefaultContext(
        bool isProtectionEnabled = true,
        bool isEmergencyPaused = false,
        SecureInputState secureInputState = SecureInputState.Inactive,
        bool isApplicationContextKnown = true,
        string processName = "",
        string windowTitle = "",
        string windowClassName = "",
        IReadOnlyList<string>? excludedApplications = null)
    {
        return new SafetyPolicyContext
        {
            IsProtectionEnabled = isProtectionEnabled,
            IsEmergencyPaused = isEmergencyPaused,
            SecureInputState = secureInputState,
            IsApplicationContextKnown = isApplicationContextKnown,
            ProcessName = processName,
            WindowTitle = windowTitle,
            WindowClassName = windowClassName,
            ExcludedApplications = excludedApplications ?? [],
            SafeModeApplications = DefaultSafeModeRules.ProcessNames,
            SafeModeWindowClasses = DefaultSafeModeRules.WindowClasses,
        };
    }
}
