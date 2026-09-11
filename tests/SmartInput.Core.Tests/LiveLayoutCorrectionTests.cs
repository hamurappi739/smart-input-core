using SmartInput.Core.Configuration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public class CurrentTokenBufferTests
{
    [Fact]
    public void Apply_Characters_BuildToken()
    {
        var buffer = new CurrentTokenBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('g'));
        buffer.Apply(TokenInputEvent.CharacterInput('h'));
        buffer.Apply(TokenInputEvent.CharacterInput('b'));

        Assert.Equal(3, buffer.Length);
    }

    [Fact]
    public void Apply_WordBoundary_CompletesToken()
    {
        var buffer = new CurrentTokenBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('g'));
        buffer.Apply(TokenInputEvent.CharacterInput('h'));
        var result = buffer.Apply(TokenInputEvent.Boundary());

        Assert.True(result.TokenCompleted);
        Assert.Equal(2, result.CompletedTokenLength);
        Assert.Equal(2, buffer.Length);
    }

    [Fact]
    public void Apply_EmptyBoundary_DoesNotComplete()
    {
        var buffer = new CurrentTokenBuffer();

        var result = buffer.Apply(TokenInputEvent.Boundary());

        Assert.False(result.TokenCompleted);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_WhitespaceCharacter_ResetsBuffer()
    {
        var buffer = new CurrentTokenBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('a'));
        var result = buffer.Apply(TokenInputEvent.CharacterInput(' '));

        Assert.True(result.BufferReset);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_PunctuationCharacter_ResetsBuffer()
    {
        var buffer = new CurrentTokenBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('a'));
        var result = buffer.Apply(TokenInputEvent.CharacterInput('.'));

        Assert.True(result.BufferReset);
        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_Backspace_RemovesLastCharacter()
    {
        var buffer = new CurrentTokenBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('a'));
        buffer.Apply(TokenInputEvent.CharacterInput('b'));
        buffer.Apply(TokenInputEvent.Backspace);

        Assert.Equal(1, buffer.Length);
    }

    [Fact]
    public void Apply_BackspaceOnEmpty_ResetsSafely()
    {
        var buffer = new CurrentTokenBuffer();

        var result = buffer.Apply(TokenInputEvent.Backspace);

        Assert.True(result.BufferReset);
        Assert.Equal(TokenBufferResetReason.UnsafeBackspace, result.ResetReason);
    }

    [Fact]
    public void Apply_Reset_ClearsBuffer()
    {
        var buffer = new CurrentTokenBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('a'));
        buffer.Apply(TokenInputEvent.Reset);

        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_Uncertain_ClearsBuffer()
    {
        var buffer = new CurrentTokenBuffer();

        buffer.Apply(TokenInputEvent.CharacterInput('a'));
        buffer.Apply(TokenInputEvent.Uncertain);

        Assert.True(buffer.IsEmpty);
    }

    [Fact]
    public void Apply_MaxLengthExceeded_DiscardsToken()
    {
        var buffer = new CurrentTokenBuffer(new CurrentTokenBufferOptions { MaxTokenLength = 3 });

        buffer.Apply(TokenInputEvent.CharacterInput('a'));
        buffer.Apply(TokenInputEvent.CharacterInput('b'));
        buffer.Apply(TokenInputEvent.CharacterInput('c'));
        var result = buffer.Apply(TokenInputEvent.CharacterInput('d'));

        Assert.True(result.BufferReset);
        Assert.Equal(TokenBufferResetReason.MaxLengthExceeded, result.ResetReason);
        Assert.True(buffer.IsEmpty);
    }
}

public class AutomaticLayoutCorrectionEngineTests
{
    [Fact]
    public async Task ProcessInputAsync_CorrectsGhbdtnAtWordBoundary()
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(
            policy: AllowedPolicy(),
            settings: EnabledSettings(),
            replacement: replacement);

        await TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        var status = engine.Status;
        Assert.Equal(1, status.TokensCompleted);
        Assert.Equal(1, status.CandidatesDetected);
        Assert.Equal(1, status.CorrectionsAttempted);
        Assert.Equal(1, status.CorrectionsSucceeded);
        Assert.Equal("ghbdtn", replacement.LastOriginal);
        Assert.Equal("привет", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_PreparedLayoutCorrection_ClearsCoreTokenBuffer()
    {
        var replacement = new FakeReplacementService(success: true);
        var recentText = new RecentTextContextBuffer();
        var engine = CreateEngine(
            policy: AllowedPolicy(),
            settings: EnabledSettings(),
            replacement: replacement,
            recentTextContext: recentText);

        await TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0),
            new PreparedLayoutCorrection
            {
                OriginalText = "ghbdtn",
                ReplacementText = "привет",
                TargetInputLanguage = KeyboardInputLanguage.Russian,
            }));

        Assert.Equal(0, engine.Status.CurrentTokenLength);
        Assert.EndsWith(" ", recentText.Snapshot(), StringComparison.Ordinal);

        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('x'));
        Assert.Equal(1, engine.Status.CurrentTokenLength);
    }

    [Theory]
    [InlineData("'nj", "nj", "это")]
    [InlineData("t;br", "br", "ежик")]
    [InlineData("[jnm", "jnm", "хоть")]
    public async Task ProcessInputAsync_PhysicalPunctuationLayout_UsesValidatedSuffixBuffer(
        string original,
        string bufferedSuffix,
        string expectedReplacement)
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), replacement);

        await TypeTokenAsync(engine, bufferedSuffix);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0),
            new PreparedLayoutCorrection
            {
                OriginalText = original,
                ReplacementText = expectedReplacement,
                TargetInputLanguage = KeyboardInputLanguage.Russian,
            }));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(original, replacement.LastOriginal);
        Assert.Equal(expectedReplacement, replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_PhysicalPunctuationLayout_RejectsUnrelatedBuffer()
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), replacement);

        await TypeTokenAsync(engine, "xx");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0),
            new PreparedLayoutCorrection
            {
                OriginalText = "'nj",
                ReplacementText = "это",
                TargetInputLanguage = KeyboardInputLanguage.Russian,
            }));

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_NonDeferredBoundary_DoesNotReplaceRecentText()
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), replacement);

        await TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(deferredBoundary: null));

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(LiveLayoutCorrectionAction.CorrectionSkippedUnsafeBoundary, engine.Status.LastAction);
        Assert.Equal(0, engine.Status.CurrentTokenLength);
    }

    [Fact]
    public async Task ProcessInputAsync_Hello_NoCorrection()
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), replacement);

        await TypeTokenAsync(engine, "hello");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(1, statusField(engine).CorrectionsBlocked);
    }

    [Fact]
    public async Task ProcessInputAsync_WaitRecommendation_DoesNotReplace()
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), replacement);

        await TypeTokenAsync(engine, "gh");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_AutomaticLayoutDisabled_SkipsCorrection()
    {
        var replacement = new FakeReplacementService(success: true);
        var settings = EnabledSettings();
        settings.AutomaticLayoutEnabled = false;
        var engine = CreateEngine(AllowedPolicy(), settings, replacement);

        await TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(LiveLayoutCorrectionAction.CorrectionSkippedDisabled, engine.Status.LastAction);
    }

    [Fact]
    public async Task ProcessInputAsync_ProtectionDisabled_SkipsCorrection()
    {
        var replacement = new FakeReplacementService(success: true);
        var settings = EnabledSettings();
        settings.IsEnabled = false;
        var engine = CreateEngine(AllowedPolicy(), settings, replacement);

        await TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    [InlineData(AutomationPolicyState.SafeMode)]
    public async Task ProcessInputAsync_NonAllowedPolicy_SkipsCorrection(AutomationPolicyState state)
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(Policy(state, allowsAutomation: false), EnabledSettings(), replacement);

        await TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(LiveLayoutCorrectionAction.CorrectionSkippedPolicy, engine.Status.LastAction);
    }

    [Fact]
    public async Task ProcessInputAsync_EmergencyPausePolicy_SkipsCorrection()
    {
        var replacement = new FakeReplacementService(success: true);
        var engine = CreateEngine(AutomationPolicyResult.EmergencyPaused(), EnabledSettings(), replacement);

        await TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task NotifyApplicationContextChanged_ClearsBuffer()
    {
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), new FakeReplacementService(true));

        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('a'));
        engine.NotifyApplicationContextChanged();

        Assert.Equal(0, engine.Status.CurrentTokenLength);
        Assert.True(engine.Status.BufferResets > 0);
    }

    [Fact]
    public async Task NotifyPolicyContextChanged_OnTransition_ClearsBuffer()
    {
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), new FakeReplacementService(true));

        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('a'));
        engine.NotifyPolicyContextChanged(Policy(AutomationPolicyState.SafeMode, allowsAutomation: false));

        Assert.Equal(0, engine.Status.CurrentTokenLength);
    }

    [Fact]
    public async Task ProcessInputAsync_UncertainInput_DiscardsPartialToken()
    {
        var engine = CreateEngine(AllowedPolicy(), EnabledSettings(), new FakeReplacementService(true));

        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('g'));
        await engine.ProcessInputAsync(TokenInputEvent.Uncertain);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, engine.Status.TokensCompleted);
    }

    private static async Task TypeTokenAsync(IAutomaticLayoutCorrectionEngine engine, string token)
    {
        foreach (var character in token)
        {
            await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
        }
    }

    private static AutomaticLayoutCorrectionEngine CreateEngine(
        AutomationPolicyResult policy,
        Core.Models.AppSettings settings,
        FakeReplacementService replacement,
        RecentTextContextBuffer? recentTextContext = null)
    {
        return LiveCorrectionTestHelpers.CreateEngine(
            policy,
            settings,
            replacement,
            recentTextContext: recentTextContext);
    }

    private static LiveLayoutCorrectionStatus statusField(AutomaticLayoutCorrectionEngine engine)
    {
        return engine.Status;
    }

    private static AutomationPolicyResult AllowedPolicy()
    {
        return Policy(AutomationPolicyState.Allowed, allowsAutomation: true);
    }

    private static AutomationPolicyResult Policy(AutomationPolicyState state, bool allowsAutomation)
    {
        return new AutomationPolicyResult
        {
            State = state,
            AllowsAutomation = allowsAutomation,
            AllowsManualExternalTextOperations = state != AutomationPolicyState.SecureInput,
        };
    }

    private static Core.Models.AppSettings EnabledSettings()
    {
        return new Core.Models.AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = true,
        };
    }

    private sealed class FakeSettingsService(Core.Models.AppSettings settings) : ISettingsService
    {
        public Core.Models.AppSettings Current { get; private set; } = settings;

        public event Action? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Action<Core.Models.AppSettings> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            return Task.CompletedTask;
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

    private sealed class FakeReplacementService(bool success) : ISafeTextReplacementService
    {
        public int CallCount { get; private set; }

        public string? LastOriginal { get; private set; }

        public string? LastReplacement { get; private set; }

        public Task<Platform.Abstractions.Text.TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOriginal = originalText;
            LastReplacement = replacementText;

            return Task.FromResult(success
                ? Platform.Abstractions.Text.TextReplacementResult.Success(originalText.Length, replacementText.Length)
                : Platform.Abstractions.Text.TextReplacementResult.Blocked("blocked"));
        }

        public Task<Platform.Abstractions.Text.TextReplacementResult> RunAbcToXyzDemoAsync(
            CancellationToken cancellationToken = default)
        {
            return ReplaceRecentTextAsync("abc", "xyz", cancellationToken);
        }

        public Task<Platform.Abstractions.Text.TextReplacementResult> InsertTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            return ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
        }
    }
}

public class LiveLayoutCorrectionCoordinatorContractTests
{
    [Fact]
    public void InputObservationRules_InjectedEventsMustNotReachTokenPipeline()
    {
        var metadata = new Platform.Abstractions.Input.KeyboardHookMetadata(
            Platform.Abstractions.Input.KeyboardHookFlags.Injected,
            0);

        Assert.False(Platform.Abstractions.Input.InputObservationRules.ShouldObserveUserInput(
            metadata,
            Platform.Abstractions.Input.SmartInputInjectionMarkers.SmartInputExtraInfo));
    }
}
