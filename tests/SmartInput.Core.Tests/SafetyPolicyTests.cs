using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Security;

namespace SmartInput.Core.Tests;

public class SafetyPolicyEvaluatorTests
{
    private readonly SafetyPolicyEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_AllowedContext_PermitsAutomationAndManualExternal()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            processName: "notepad");

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.Allowed, result.State);
        Assert.True(result.AllowsAutomation);
        Assert.True(result.AllowsManualExternalTextOperations);
        Assert.False(result.IsEmergencyPaused);
    }

    [Fact]
    public void Evaluate_ProtectionDisabled_BlocksAllOperationsBeforeOtherPolicyChecks()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            isProtectionEnabled: false,
            secureInputState: SecureInputState.Active,
            processName: "notepad");

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.ProtectionDisabled, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.False(result.AllowsManualExternalTextOperations);
    }

    [Fact]
    public void Evaluate_EmergencyPause_BlocksAllOperations()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            isEmergencyPaused: true,
            processName: "notepad");

        var result = _evaluator.Evaluate(context);

        Assert.True(result.IsEmergencyPaused);
        Assert.False(result.AllowsAutomation);
        Assert.False(result.AllowsManualExternalTextOperations);
        Assert.Equal(AutomationPolicyState.UnknownContext, result.State);
    }

    [Fact]
    public void Evaluate_SecureInputActive_BlocksAllOperations()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            secureInputState: SecureInputState.Active,
            processName: "notepad");

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.SecureInput, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.False(result.AllowsManualExternalTextOperations);
    }

    [Fact]
    public void Evaluate_SecureInputUnknown_BlocksAllOperations()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            secureInputState: SecureInputState.Unknown,
            processName: "notepad");

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.UnknownContext, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.False(result.AllowsManualExternalTextOperations);
    }

    [Fact]
    public void Evaluate_UnknownApplicationContext_BlocksAllOperations()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            isApplicationContextKnown: false);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.UnknownContext, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.False(result.AllowsManualExternalTextOperations);
    }

    [Fact]
    public void Evaluate_ExcludedApplication_BlocksAllOperations()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            processName: "bankapp",
            excludedApplications: ["bankapp"]);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.BlockedApplication, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.False(result.AllowsManualExternalTextOperations);
    }

    [Theory]
    [InlineData("WindowsTerminal")]
    [InlineData("powershell")]
    [InlineData("cmd")]
    public void Evaluate_DefaultSafeModeProcess_BlocksAutomationOnly(string processName)
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(processName: processName);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.SafeMode, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.True(result.AllowsManualExternalTextOperations);
    }

    [Theory]
    [InlineData("Cursor")]
    [InlineData("Code")]
    [InlineData("devenv")]
    [InlineData("rider64")]
    public void Evaluate_CodeEditors_AreAllowedByDefault(string processName)
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(processName: processName);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.Allowed, result.State);
        Assert.True(result.AllowsAutomation);
    }

    [Fact]
    public void Evaluate_CefApplications_AreAllowedByDefault()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(windowClassName: "CEFCLIENT");

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.Allowed, result.State);
        Assert.True(result.AllowsAutomation);
    }

    [Theory]
    [InlineData("UnityWndClass")]
    [InlineData("UnrealWindow")]
    [InlineData("SDL_app")]
    public void Evaluate_IdentifiableGameWindowClass_BlocksAutomationOnly(string windowClass)
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            processName: "game",
            windowClassName: windowClass);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.SafeMode, result.State);
        Assert.False(result.AllowsAutomation);
        Assert.True(result.AllowsManualExternalTextOperations);
    }

    [Fact]
    public void Evaluate_Precedence_EmergencyPauseOverridesSecureInput()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            isEmergencyPaused: true,
            secureInputState: SecureInputState.Active,
            processName: "notepad");

        var result = _evaluator.Evaluate(context);

        Assert.True(result.IsEmergencyPaused);
        Assert.Equal(AutomationPolicyState.UnknownContext, result.State);
    }

    [Fact]
    public void Evaluate_Precedence_SecureInputOverridesExcludedApplication()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            secureInputState: SecureInputState.Active,
            processName: "bankapp",
            excludedApplications: ["bankapp"]);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.SecureInput, result.State);
    }

    [Fact]
    public void Evaluate_Precedence_ExcludedApplicationOverridesSafeMode()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            processName: "Code",
            excludedApplications: ["Code"]);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.BlockedApplication, result.State);
        Assert.False(result.AllowsManualExternalTextOperations);
    }

    [Fact]
    public void Evaluate_Precedence_UnknownContextOverridesSafeMode()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            isApplicationContextKnown: false,
            processName: "Code");

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.UnknownContext, result.State);
        Assert.False(result.AllowsManualExternalTextOperations);
    }

    [Fact]
    public void Evaluate_ExcludedApplicationMatch_IsCaseInsensitive()
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            processName: "BankApp",
            excludedApplications: ["bankapp"]);

        var result = _evaluator.Evaluate(context);

        Assert.Equal(AutomationPolicyState.BlockedApplication, result.State);
    }
}

public class AutomationSafetyServiceTests
{
    [Fact]
    public async Task EvaluateCurrentContextAsync_UsesPlatformSignalsAndSettings()
    {
        var settings = new SettingsService(new InMemorySettingsPersistence());
        settings.Current.ExcludedApplications.Add("blocked");

        var service = new AutomationSafetyService(
            new SafetyPolicyEvaluator(),
            new FakeSecureInputDetector(SecureInputState.Inactive),
            new FakeActiveApplicationService("blocked", "Blocked", "Class"),
            new FakeEmergencyPauseService(isPaused: false),
            settings);

        var result = await service.EvaluateCurrentContextAsync();

        Assert.Equal(AutomationPolicyState.BlockedApplication, result.State);
    }

    [Fact]
    public async Task IsOperationAllowedAsync_SafeModeAllowsManualExternalOnly()
    {
        var service = CreateService(processName: "powershell");

        var automationAllowed = await service.IsOperationAllowedAsync(
            AutomationOperationKind.AutomaticTextReplacement);
        var manualAllowed = await service.IsOperationAllowedAsync(
            AutomationOperationKind.ManualExternalTextOperation);

        Assert.False(automationAllowed);
        Assert.True(manualAllowed);
    }

    [Fact]
    public async Task IsOperationAllowedAsync_ProtectionDisabled_BlocksDirectTextReplacement()
    {
        var settings = new SettingsService(new InMemorySettingsPersistence());
        settings.Current.IsEnabled = false;
        var service = new AutomationSafetyService(
            new SafetyPolicyEvaluator(),
            new FakeSecureInputDetector(SecureInputState.Inactive),
            new FakeActiveApplicationService("notepad", "Title", "Class"),
            new FakeEmergencyPauseService(isPaused: false),
            settings);

        var policy = await service.EvaluateCurrentContextAsync();
        var allowed = await service.IsOperationAllowedAsync(
            AutomationOperationKind.AutomaticTextReplacement);

        Assert.Equal(AutomationPolicyState.ProtectionDisabled, policy.State);
        Assert.False(allowed);
    }

    private static AutomationSafetyService CreateService(
        SecureInputState secureInputState = SecureInputState.Inactive,
        string processName = "notepad",
        bool isPaused = false,
        bool applicationKnown = true)
    {
        var settings = new SettingsService(new InMemorySettingsPersistence());

        return new AutomationSafetyService(
            new SafetyPolicyEvaluator(),
            new FakeSecureInputDetector(secureInputState),
            new FakeActiveApplicationService(
                applicationKnown ? processName : null,
                "Title",
                "Class"),
            new FakeEmergencyPauseService(isPaused),
            settings);
    }

    private sealed class InMemorySettingsPersistence : Core.Persistence.ISettingsPersistence
    {
        public Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AppSettings?>(null);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSecureInputDetector(SecureInputState state) : ISecureInputDetector
    {
        public Task<SecureInputDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SecureInputDetectionResult(state));
        }
    }

    private sealed class FakeActiveApplicationService : Platform.Abstractions.Application.IActiveApplicationService
    {
        private readonly string? _processName;

        public FakeActiveApplicationService(string? processName, string title, string className)
        {
            _processName = processName;
            WindowTitle = title;
            WindowClassName = className;
        }

        public string WindowTitle { get; }

        public string WindowClassName { get; }

        public Task<Platform.Abstractions.Application.ActiveApplicationInfo?> GetActiveApplicationAsync(
            CancellationToken cancellationToken = default)
        {
            if (_processName is null)
            {
                return Task.FromResult<Platform.Abstractions.Application.ActiveApplicationInfo?>(null);
            }

            return Task.FromResult<Platform.Abstractions.Application.ActiveApplicationInfo?>(
                new Platform.Abstractions.Application.ActiveApplicationInfo(
                    _processName,
                    WindowTitle,
                    WindowClassName,
                    0));
        }
    }

    private sealed class FakeEmergencyPauseService(bool isPaused) : Platform.Abstractions.Safety.IEmergencyPauseService
    {
        public bool IsPaused { get; private set; } = isPaused;

        public void Pause() => IsPaused = true;

        public void Resume() => IsPaused = false;
    }
}
