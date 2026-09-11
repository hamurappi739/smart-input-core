using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Platform.Abstractions.Application;
using SmartInput.Platform.Abstractions.Safety;
using SmartInput.Platform.Abstractions.Security;

namespace SmartInput.Core.Services;

public sealed class AutomationSafetyService : IAutomationSafetyService
{
    private readonly ISafetyPolicyEvaluator _policyEvaluator;
    private readonly ISecureInputDetector _secureInputDetector;
    private readonly IActiveApplicationService _activeApplicationService;
    private readonly IEmergencyPauseService _emergencyPauseService;
    private readonly ISettingsService _settingsService;

    public AutomationSafetyService(
        ISafetyPolicyEvaluator policyEvaluator,
        ISecureInputDetector secureInputDetector,
        IActiveApplicationService activeApplicationService,
        IEmergencyPauseService emergencyPauseService,
        ISettingsService settingsService)
    {
        _policyEvaluator = policyEvaluator;
        _secureInputDetector = secureInputDetector;
        _activeApplicationService = activeApplicationService;
        _emergencyPauseService = emergencyPauseService;
        _settingsService = settingsService;
    }

    public AutomationPolicyResult EvaluateCurrentContext()
    {
        return EvaluateCurrentContextAsync().GetAwaiter().GetResult();
    }

    public async Task<AutomationPolicyResult> EvaluateCurrentContextAsync(
        CancellationToken cancellationToken = default)
    {
        var activeApplication = await _activeApplicationService
            .GetActiveApplicationAsync(cancellationToken)
            .ConfigureAwait(false);

        return await EvaluateCurrentContextAsync(activeApplication, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AutomationPolicyResult> EvaluateCurrentContextAsync(
        ActiveApplicationInfo? activeApplication,
        CancellationToken cancellationToken = default)
    {
        var secureInput = await _secureInputDetector
            .DetectAsync(cancellationToken)
            .ConfigureAwait(false);

        var context = BuildContext(secureInput.State, activeApplication);
        return _policyEvaluator.Evaluate(context);
    }

    public bool IsOperationAllowed(AutomationOperationKind operationKind)
    {
        return EvaluateCurrentContext().IsAllowed(operationKind);
    }

    public async Task<bool> IsOperationAllowedAsync(
        AutomationOperationKind operationKind,
        CancellationToken cancellationToken = default)
    {
        var policy = await EvaluateCurrentContextAsync(cancellationToken).ConfigureAwait(false);
        return policy.IsAllowed(operationKind);
    }

    public string? GetBlockedReason(AutomationOperationKind operationKind)
    {
        var policy = EvaluateCurrentContext();
        return policy.IsAllowed(operationKind) ? null : policy.Reason;
    }

    private SafetyPolicyContext BuildContext(
        SecureInputState secureInputState,
        ActiveApplicationInfo? activeApplication)
    {
        return new SafetyPolicyContext
        {
            IsProtectionEnabled = _settingsService.Current.IsEnabled,
            IsEmergencyPaused = _emergencyPauseService.IsPaused,
            SecureInputState = secureInputState,
            IsApplicationContextKnown = activeApplication is not null,
            ProcessName = activeApplication?.ProcessName ?? string.Empty,
            WindowTitle = activeApplication?.WindowTitle ?? string.Empty,
            WindowClassName = activeApplication?.WindowClassName ?? string.Empty,
            ExcludedApplications = _settingsService.Current.ExcludedApplications,
            SafeModeApplications = DefaultSafeModeRules.ProcessNames,
            SafeModeWindowClasses = DefaultSafeModeRules.WindowClasses,
        };
    }
}
