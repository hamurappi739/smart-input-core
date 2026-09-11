namespace SmartInput.Core.Models;

public sealed class AutomationPolicyResult
{
    public AutomationPolicyState State { get; init; }

    public bool IsEmergencyPaused { get; init; }

    public bool AllowsAutomation { get; init; }

    public bool AllowsManualExternalTextOperations { get; init; }

    public string? Reason { get; init; }

    public static AutomationPolicyResult EmergencyPaused()
    {
        return new AutomationPolicyResult
        {
            State = AutomationPolicyState.UnknownContext,
            IsEmergencyPaused = true,
            AllowsAutomation = false,
            AllowsManualExternalTextOperations = false,
            Reason = "Активна экстренная пауза. Автоматизация отключена.",
        };
    }

    public bool IsAllowed(AutomationOperationKind operationKind)
    {
        return operationKind switch
        {
            AutomationOperationKind.ManualExternalTextOperation => AllowsManualExternalTextOperations,
            _ => AllowsAutomation,
        };
    }
}
