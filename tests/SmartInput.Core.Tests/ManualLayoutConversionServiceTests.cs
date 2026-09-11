using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class ManualLayoutConversionServiceTests
{
    [Fact]
    public async Task ConvertSelectedTextAsync_BlocksSecureInput()
    {
        var service = CreateService(
            new FakeSelectedTextService("ghbdtn"),
            AutomationPolicyResultFactory.SecureInput());

        var result = await service.ConvertSelectedTextAsync(LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(LayoutConversionStatus.Blocked, result.Status);
        Assert.Equal(ManualLayoutConversionService.SecureInputBlockedReason, result.FailureReason);
    }

    [Fact]
    public async Task ConvertSelectedTextAsync_BlocksEmergencyPause()
    {
        var service = CreateService(
            new FakeSelectedTextService("ghbdtn"),
            AutomationPolicyResultFactory.EmergencyPaused());

        var result = await service.ConvertSelectedTextAsync(LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(LayoutConversionStatus.Blocked, result.Status);
    }

    [Fact]
    public async Task ConvertSelectedTextAsync_AllowsSafeModeManualExternal()
    {
        var selectedTextService = new FakeSelectedTextService("ghbdtn");
        var service = CreateService(
            selectedTextService,
            AutomationPolicyResultFactory.SafeMode());

        var result = await service.ConvertSelectedTextAsync(LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(LayoutConversionStatus.Success, result.Status);
        Assert.Equal("привет", selectedTextService.LastReplacement);
    }

    [Fact]
    public async Task ConvertSelectedTextAsync_ReturnsNoSelectionWhenEmpty()
    {
        var service = CreateService(
            new FakeSelectedTextService(null),
            AutomationPolicyResultFactory.Allowed());

        var result = await service.ConvertSelectedTextAsync(LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(LayoutConversionStatus.NoSelection, result.Status);
    }

    [Fact]
    public async Task ConvertSelectedTextAsync_ReplacesSelectedTextWithoutLogging()
    {
        var selectedTextService = new FakeSelectedTextService("ghbdtn");
        var service = CreateService(
            selectedTextService,
            AutomationPolicyResultFactory.Allowed());

        var result = await service.ConvertSelectedTextAsync(LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(LayoutConversionStatus.Success, result.Status);
        Assert.Equal("привет".Length, result.CharacterCount);
        Assert.Equal("привет", selectedTextService.LastReplacement);
    }

    private static ManualLayoutConversionService CreateService(
        FakeSelectedTextService selectedTextService,
        AutomationPolicyResult policy)
    {
        return new ManualLayoutConversionService(
            new KeyboardLayoutConverter(),
            selectedTextService,
            new FakeAutomationSafetyService(policy));
    }

    private sealed class FakeSelectedTextService : ISelectedTextService
    {
        private readonly string? _selectedText;

        public FakeSelectedTextService(string? selectedText)
        {
            _selectedText = selectedText;
        }

        public string? LastReplacement { get; private set; }

        public Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_selectedText);
        }

        public Task<bool> ReplaceSelectedTextAsync(string replacementText, CancellationToken cancellationToken = default)
        {
            LastReplacement = replacementText;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeAutomationSafetyService(AutomationPolicyResult policy) : IAutomationSafetyService
    {
        public AutomationPolicyResult EvaluateCurrentContext() => policy;

        public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policy);
        }

        public bool IsOperationAllowed(AutomationOperationKind operationKind) => policy.IsAllowed(operationKind);

        public Task<bool> IsOperationAllowedAsync(
            AutomationOperationKind operationKind,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policy.IsAllowed(operationKind));
        }

        public string? GetBlockedReason(AutomationOperationKind operationKind)
        {
            return policy.IsAllowed(operationKind) ? null : policy.Reason;
        }
    }

    private static class AutomationPolicyResultFactory
    {
        public static AutomationPolicyResult Allowed()
        {
            return new AutomationPolicyResult
            {
                State = AutomationPolicyState.Allowed,
                AllowsAutomation = true,
                AllowsManualExternalTextOperations = true,
            };
        }

        public static AutomationPolicyResult SafeMode()
        {
            return new AutomationPolicyResult
            {
                State = AutomationPolicyState.SafeMode,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = true,
                Reason = "Safe Mode is active for this application category.",
            };
        }

        public static AutomationPolicyResult SecureInput()
        {
            return new AutomationPolicyResult
            {
                State = AutomationPolicyState.SecureInput,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
                Reason = "Secure input is active in the foreground context.",
            };
        }

        public static AutomationPolicyResult EmergencyPaused()
        {
            return AutomationPolicyResult.EmergencyPaused();
        }
    }
}
