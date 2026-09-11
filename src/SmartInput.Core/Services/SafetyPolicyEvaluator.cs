using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Security;

namespace SmartInput.Core.Services;

public sealed class SafetyPolicyEvaluator : ISafetyPolicyEvaluator
{
    public AutomationPolicyResult Evaluate(SafetyPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.IsProtectionEnabled)
        {
            return Blocked(
                AutomationPolicyState.ProtectionDisabled,
                "Защита Smart Input выключена.");
        }

        if (context.IsEmergencyPaused)
        {
            return AutomationPolicyResult.EmergencyPaused();
        }

        if (context.SecureInputState == SecureInputState.Active)
        {
            return Blocked(
                AutomationPolicyState.SecureInput,
                "В активном контексте используется защищённый ввод.");
        }

        if (context.SecureInputState == SecureInputState.Unknown
            || !context.IsApplicationContextKnown)
        {
            return Blocked(
                AutomationPolicyState.UnknownContext,
                "Не удалось надёжно определить контекст ввода.");
        }

        if (ApplicationRuleMatcher.IsExcluded(context.ProcessName, context.ExcludedApplications))
        {
            return Blocked(
                AutomationPolicyState.BlockedApplication,
                "Активное приложение исключено из автоматизации.");
        }

        if (ApplicationRuleMatcher.IsSafeMode(
                context.ProcessName,
                context.WindowClassName,
                context.SafeModeApplications,
                context.SafeModeWindowClasses))
        {
            return new AutomationPolicyResult
            {
                State = AutomationPolicyState.SafeMode,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = true,
                Reason = "Для этой категории приложений активен безопасный режим.",
            };
        }

        return Allowed();
    }

    private static AutomationPolicyResult Allowed()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.Allowed,
            AllowsAutomation = true,
            AllowsManualExternalTextOperations = true,
        };
    }

    private static AutomationPolicyResult Blocked(AutomationPolicyState state, string reason)
    {
        return new AutomationPolicyResult
        {
            State = state,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = false,
            Reason = reason,
        };
    }
}

internal static class ApplicationRuleMatcher
{
    internal static bool IsExcluded(string processName, IReadOnlyList<string> excludedApplications)
    {
        if (excludedApplications.Count == 0 || string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        foreach (var excluded in excludedApplications)
        {
            if (string.IsNullOrWhiteSpace(excluded))
            {
                continue;
            }

            if (processName.Equals(excluded.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsSafeMode(
        string processName,
        string windowClassName,
        IReadOnlyList<string> safeModeApplications,
        IReadOnlyList<string> safeModeWindowClasses)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            foreach (var safeModeProcess in safeModeApplications)
            {
                if (string.IsNullOrWhiteSpace(safeModeProcess))
                {
                    continue;
                }

                if (processName.Equals(safeModeProcess.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(windowClassName))
        {
            foreach (var safeModeClass in safeModeWindowClasses)
            {
                if (string.IsNullOrWhiteSpace(safeModeClass))
                {
                    continue;
                }

                if (windowClassName.Equals(safeModeClass.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
