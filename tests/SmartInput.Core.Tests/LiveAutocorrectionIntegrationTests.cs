using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class LiveAutocorrectionIntegrationTests
{
    [Fact]
    public async Task ProcessInputAsync_RussianTypo_CorrectsPrivetAtBoundary()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "превет");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("превет", replacement.LastOriginal);
        Assert.Equal("привет", replacement.LastReplacement);
        Assert.Equal(1, engine.Status.AutocorrectSucceeded);
    }

    [Fact]
    public async Task ProcessInputAsync_EnglishTypo_CorrectsHelloAtBoundary()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("helo", replacement.LastOriginal);
        Assert.Equal("hello", replacement.LastReplacement);
        Assert.Equal(1, engine.Status.AutocorrectSucceeded);
    }

    [Theory]
    [InlineData("ппочему", "почему")]
    [InlineData("ннормально", "нормально")]
    [InlineData("ппиздец", "пиздец")]
    [InlineData("сстранно", "странно")]
    public async Task ProcessInputAsync_DuplicateInitialLetter_UsesGenericRecovery(
        string source,
        string expected)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, source);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(source, replacement.LastOriginal);
        Assert.Equal(expected, replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_CorrectRussianWord_NoAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "привет");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(1, engine.Status.AutocorrectBlocked);
    }

    [Fact]
    public async Task ProcessInputAsync_CorrectEnglishWord_NoAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "hello");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_LayoutCandidateHasPriorityOverAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("ghbdtn", replacement.LastOriginal);
        Assert.Equal("привет", replacement.LastReplacement);
        Assert.Equal(1, engine.Status.CorrectionsSucceeded);
        Assert.Equal(0, engine.Status.AutocorrectAttempts);
    }

    [Fact]
    public async Task ProcessInputAsync_LayoutNoChange_AllowsAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("hello", replacement.LastReplacement);
        Assert.Equal(1, engine.Status.AutocorrectSucceeded);
    }

    [Fact]
    public async Task ProcessInputAsync_AutocorrectDisabled_SkipsAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(LiveLayoutCorrectionAction.AutocorrectSkippedDisabled, engine.Status.LastAction);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    [InlineData(AutomationPolicyState.SafeMode)]
    public async Task ProcessInputAsync_NonAllowedPolicy_SkipsAutocorrect(AutomationPolicyState state)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(state, allowsAutomation: false),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(LiveLayoutCorrectionAction.CorrectionSkippedPolicy, engine.Status.LastAction);
    }

    [Fact]
    public async Task ProcessInputAsync_EmergencyPause_SkipsAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            AutomationPolicyResult.EmergencyPaused(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_ReplacementAbort_DeliversBoundaryOnce()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService((original, candidate) =>
            TextReplacementResult.AbortedByUserInput(original.Length, candidate.Length));
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            delivery);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal(LiveLayoutCorrectionAction.AutocorrectFailed, engine.Status.LastAction);
    }

    [Fact]
    public async Task ProcessInputAsync_DoesNotChainLayoutAndAutocorrectReplacements()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_LayoutFailure_DoesNotChainAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: false);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(0, engine.Status.AutocorrectAttempts);
    }

    [Fact]
    public async Task ProcessInputAsync_BoundaryDeliveredOnceAfterAutocorrectSuccess()
    {
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            new LiveCorrectionTestHelpers.FakeReplacementService(success: true),
            delivery);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal(1, engine.Status.BoundariesDelivered);
    }
}
