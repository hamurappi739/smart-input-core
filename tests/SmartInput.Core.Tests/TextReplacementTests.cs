using SmartInput.Core.Diagnostics;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Security;
using SmartInput.Platform.Abstractions.Text;
using SmartInput.Platform.Windows.Native;
using SmartInput.Platform.Windows.Services;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.Core.Tests;

public class InputObservationRulesTests
{
    private const nuint SmartInputMarker = SmartInputInjectionMarkers.SmartInputExtraInfo;

    [Fact]
    public void IsInjectedInput_ReturnsTrueForInjectedFlag()
    {
        var metadata = new KeyboardHookMetadata(KeyboardHookFlags.Injected, 0);

        Assert.True(InputObservationRules.IsInjectedInput(metadata, SmartInputMarker));
        Assert.False(InputObservationRules.ShouldObserveUserInput(metadata, SmartInputMarker));
    }

    [Fact]
    public void IsInjectedInput_ReturnsTrueForSmartInputMarker()
    {
        var metadata = new KeyboardHookMetadata(0, SmartInputMarker);

        Assert.True(InputObservationRules.IsInjectedInput(metadata, SmartInputMarker));
        Assert.False(InputObservationRules.ShouldObserveUserInput(metadata, SmartInputMarker));
    }

    [Fact]
    public void ShouldObserveUserInput_ReturnsTrueForGenuineUserEvents()
    {
        var metadata = new KeyboardHookMetadata(0, 0);

        Assert.False(InputObservationRules.IsInjectedInput(metadata, SmartInputInjectionMarkers.SmartInputExtraInfo));
        Assert.True(InputObservationRules.ShouldObserveUserInput(metadata, SmartInputInjectionMarkers.SmartInputExtraInfo));
    }

    [Fact]
    public void ShouldAbortReplacementOnUserInput_OnlyForGenuineKeyDown()
    {
        Assert.True(InputObservationRules.ShouldAbortReplacementOnUserInput(PlatformKeyEventType.KeyDown, isInjectedInput: false));
        Assert.False(InputObservationRules.ShouldAbortReplacementOnUserInput(PlatformKeyEventType.KeyUp, isInjectedInput: false));
        Assert.False(InputObservationRules.ShouldAbortReplacementOnUserInput(PlatformKeyEventType.KeyDown, isInjectedInput: true));
    }
}

public class WindowsReplacementBatchTests
{
    [Fact]
    public void BuildReplacementInputs_IsContiguousBackspaceThenUnicode()
    {
        var batch = WindowsTextReplacementService.BuildReplacementInputs("abc", "xyz");

        Assert.Equal(12, batch.Length);
        Assert.All(batch.Take(6), input => Assert.Equal(Win32Input.VkBack, input.Data.Keyboard.VirtualKey));
        Assert.All(batch.Skip(6), input => Assert.Equal(Win32Input.KeyeventfUnicode, input.Data.Keyboard.Flags & Win32Input.KeyeventfUnicode));
        Assert.Equal((ushort)'x', batch[6].Data.Keyboard.ScanCode);
        Assert.Equal((ushort)'y', batch[8].Data.Keyboard.ScanCode);
        Assert.Equal((ushort)'z', batch[10].Data.Keyboard.ScanCode);
        Assert.Equal(Win32Input.KeyeventfKeyUp, batch[1].Data.Keyboard.Flags);
        Assert.Equal(Win32Input.KeyeventfKeyUp, batch[7].Data.Keyboard.Flags & Win32Input.KeyeventfKeyUp);
    }
}

public class TextReplacementSessionStateTests
{
    [Fact]
    public void NotifyKeyboardEvent_AbortsOnConcurrentGenuineKeyDown()
    {
        var state = new TextReplacementSessionState();

        using (state.BeginSession())
        {
            state.NotifyKeyboardEvent(PlatformKeyEventType.KeyDown, isInjectedInput: false);

            Assert.True(state.AbortedByUserInput);
            Assert.False(state.ShouldContinueReplacement);
        }
    }

    [Fact]
    public void NotifyKeyboardEvent_DoesNotAbortForInjectedKeyDown()
    {
        var state = new TextReplacementSessionState();

        using (state.BeginSession())
        {
            state.NotifyKeyboardEvent(PlatformKeyEventType.KeyDown, isInjectedInput: true);
            state.NotifyKeyboardEvent(PlatformKeyEventType.KeyUp, isInjectedInput: false);

            Assert.False(state.AbortedByUserInput);
            Assert.True(state.ShouldContinueReplacement);
        }
    }

    [Fact]
    public void NotifyKeyboardEvent_DoesNotAbortWhenSessionInactive()
    {
        var state = new TextReplacementSessionState();

        state.NotifyKeyboardEvent(PlatformKeyEventType.KeyDown, isInjectedInput: false);

        Assert.False(state.AbortedByUserInput);
    }

    [Fact]
    public void BeginSession_AllowsReplacementUntilUserKeyDown()
    {
        var state = new TextReplacementSessionState();

        using (state.BeginSession())
        {
            Assert.True(state.ShouldContinueReplacement);

            state.NotifyKeyboardEvent(PlatformKeyEventType.KeyDown, isInjectedInput: false);

            Assert.True(state.AbortedByUserInput);
        }

        Assert.False(state.IsActive);
    }
}

public class TextReplacementValidatorTests
{
    [Fact]
    public void Validate_AcceptsDemoStrings()
    {
        var result = TextReplacementValidator.Validate("abc", "xyz");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsEmptyOriginalText(string originalText)
    {
        var result = TextReplacementValidator.Validate(originalText, "xyz");

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Validate_RejectsControlCharacters()
    {
        var result = TextReplacementValidator.Validate("ab\nc", "xyz");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsOversizedText()
    {
        var original = new string('a', TextReplacementValidator.MaxTextLength + 1);
        var result = TextReplacementValidator.Validate(original, "x");

        Assert.False(result.IsValid);
    }
}

public class SafeTextReplacementServiceTests
{
    [Fact]
    public async Task ReplaceRecentTextAsync_BlocksInvalidRequests()
    {
        var platform = new FakeTextReplacementService();
        var service = new SafeTextReplacementService(platform, AutomationSafety.Allowed(), NullPerformanceMetricsRecorder.Instance);

        var result = await service.ReplaceRecentTextAsync(string.Empty, "xyz");

        Assert.Equal(TextReplacementStatus.Blocked, result.Status);
        Assert.Equal(0, platform.CallCount);
    }

    [Fact]
    public async Task ReplaceRecentTextAsync_BlocksSecureInput()
    {
        var platform = new FakeTextReplacementService();
        var service = new SafeTextReplacementService(platform, AutomationSafety.SecureInput(), NullPerformanceMetricsRecorder.Instance);

        var result = await service.ReplaceRecentTextAsync("abc", "xyz");

        Assert.Equal(TextReplacementStatus.Blocked, result.Status);
        Assert.Equal(SafeTextReplacementService.SecureInputBlockedReason, result.FailureReason);
        Assert.Equal(0, platform.CallCount);
    }

    [Fact]
    public async Task ReplaceRecentTextAsync_BlocksSafeModeAutomation()
    {
        var platform = new FakeTextReplacementService();
        var service = new SafeTextReplacementService(platform, AutomationSafety.SafeMode(), NullPerformanceMetricsRecorder.Instance);

        var result = await service.ReplaceRecentTextAsync("abc", "xyz");

        Assert.Equal(TextReplacementStatus.Blocked, result.Status);
        Assert.StartsWith(SafeTextReplacementService.PolicyBlockedReasonPrefix, result.FailureReason);
        Assert.Equal(0, platform.CallCount);
    }

    [Fact]
    public async Task ReplaceRecentTextAsync_BlocksProtectionDisabled()
    {
        var platform = new FakeTextReplacementService();
        var service = new SafeTextReplacementService(platform, AutomationSafety.ProtectionDisabled(), NullPerformanceMetricsRecorder.Instance);

        var result = await service.ReplaceRecentTextAsync("abc", "xyz");

        Assert.Equal(TextReplacementStatus.Blocked, result.Status);
        Assert.StartsWith(SafeTextReplacementService.PolicyBlockedReasonPrefix, result.FailureReason);
        Assert.Equal(0, platform.CallCount);
    }

    [Fact]
    public async Task RunAbcToXyzDemoAsync_UsesDemoConstants()
    {
        var platform = new FakeTextReplacementService();
        var service = new SafeTextReplacementService(platform, AutomationSafety.Allowed(), NullPerformanceMetricsRecorder.Instance);

        var result = await service.RunAbcToXyzDemoAsync();

        Assert.Equal(TextReplacementStatus.Success, result.Status);
        Assert.Equal("abc", platform.LastRequest?.OriginalText);
        Assert.Equal("xyz", platform.LastRequest?.ReplacementText);
    }

    private sealed class FakeTextReplacementService : ITextReplacementService
    {
        public int CallCount { get; private set; }

        public TextReplacementRequest? LastRequest { get; private set; }

        public Task<TextReplacementResult> ReplaceRecentTextAsync(
            TextReplacementRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(TextReplacementResult.Success(
                request.OriginalText.Length,
                request.ReplacementText.Length));
        }
    }

    private static class AutomationSafety
    {
        public static IAutomationSafetyService Allowed()
        {
            return Create(new AutomationPolicyResult
            {
                State = AutomationPolicyState.Allowed,
                AllowsAutomation = true,
                AllowsManualExternalTextOperations = true,
            });
        }

        public static IAutomationSafetyService SecureInput()
        {
            return Create(new AutomationPolicyResult
            {
                State = AutomationPolicyState.SecureInput,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
                Reason = "Secure input is active in the foreground context.",
            });
        }

        public static IAutomationSafetyService SafeMode()
        {
            return Create(new AutomationPolicyResult
            {
                State = AutomationPolicyState.SafeMode,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = true,
                Reason = "Safe Mode is active for this application category.",
            });
        }

        public static IAutomationSafetyService ProtectionDisabled()
        {
            return Create(new AutomationPolicyResult
            {
                State = AutomationPolicyState.ProtectionDisabled,
                AllowsAutomation = false,
                AllowsManualExternalTextOperations = false,
                Reason = "Smart Input protection is disabled.",
            });
        }

        private static IAutomationSafetyService Create(AutomationPolicyResult policy)
        {
            return new FakeAutomationSafetyService(policy);
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
}

public class InputObservationFilterContractTests
{
    [Fact]
    public void KeyboardHookFlags_InjectedMatchesWindowsLowLevelFlag()
    {
        Assert.Equal(0x10u, KeyboardHookFlags.Injected);
    }

    [Fact]
    public void KeyboardHookMetadata_StoresHookValues()
    {
        var metadata = new KeyboardHookMetadata(0x10, SmartInputInjectionMarkers.SmartInputExtraInfo);

        Assert.Equal(0x10u, metadata.Flags);
        Assert.Equal(SmartInputInjectionMarkers.SmartInputExtraInfo, metadata.ExtraInfo);
    }
}
