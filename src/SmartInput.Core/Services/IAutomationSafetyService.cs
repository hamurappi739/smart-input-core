using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Application;

namespace SmartInput.Core.Services;

public interface IAutomationSafetyService
{
    AutomationPolicyResult EvaluateCurrentContext();

    Task<AutomationPolicyResult> EvaluateCurrentContextAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a previously captured foreground snapshot without querying
    /// the active application a second time. The default implementation keeps
    /// test and third-party implementations source-compatible.
    /// </summary>
    Task<AutomationPolicyResult> EvaluateCurrentContextAsync(
        ActiveApplicationInfo? activeApplication,
        CancellationToken cancellationToken = default)
        => EvaluateCurrentContextAsync(cancellationToken);

    bool IsOperationAllowed(AutomationOperationKind operationKind);

    Task<bool> IsOperationAllowedAsync(
        AutomationOperationKind operationKind,
        CancellationToken cancellationToken = default);

    string? GetBlockedReason(AutomationOperationKind operationKind);
}
