using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class PunctuationCorrectionSafetyTests
{
    [Fact]
    public async Task ProcessInputAsync_PositiveCase_RemovesSpaceBeforeComma()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = CreateEnabledEngine(replacement, delivery);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", ',');

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(" ", replacement.LastOriginal);
        Assert.Equal(string.Empty, replacement.LastReplacement);
        Assert.Equal(2, delivery.DeliverCallCount);
        Assert.Equal(1, engine.Status.PunctuationSucceeded);
        Assert.Equal(LiveLayoutCorrectionAction.PunctuationSucceeded, engine.Status.LastAction);
    }

    [Fact]
    public async Task ProcessInputAsync_ProtectionDisabled_NoModification()
    {
        var settings = LiveCorrectionTestHelpers.EnabledPunctuationSettings();
        settings.IsEnabled = false;
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            settings,
            replacement);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", ',');

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, engine.Status.PunctuationAttempts);
    }

    [Fact]
    public async Task ProcessInputAsync_PunctuationDisabled_NoModification()
    {
        var settings = LiveCorrectionTestHelpers.EnabledPunctuationSettings();
        settings.PunctuationEnabled = false;
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            settings,
            replacement);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", ',');

        Assert.Equal(0, replacement.CallCount);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    [InlineData(AutomationPolicyState.SafeMode)]
    public async Task ProcessInputAsync_BlockedPolicy_NoModification(AutomationPolicyState state)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(state, allowsAutomation: false),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", ',');

        Assert.Equal(0, replacement.CallCount);
        Assert.True(engine.Status.PunctuationBlocked >= 1 || engine.Status.CorrectionsBlocked >= 1);
    }

    [Fact]
    public async Task ProcessInputAsync_EmergencyPaused_NoModification()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            AutomationPolicyResult.EmergencyPaused(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTextSpaceThenPunctuationAsync(engine, "Привет", ',');

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_UnsafeBoundary_NoModification()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = CreateEnabledEngine(replacement, new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService());

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "Привет");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(LiveCorrectionTestHelpers.SpaceBoundaryKey()));
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_DecimalPreserved_NoModification()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = CreateEnabledEngine(replacement, new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService());

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "3.14");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemPeriod, 0, '.')));

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task CanApplyPunctuationCorrection_WhenAllowed_ReturnsTrue()
    {
        var recentText = new RecentTextContextBuffer();
        var service = new PunctuationCorrectionService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement,
            recentTextContext: recentText,
            punctuationCorrectionService: service);

        recentText.Apply(TokenInputEvent.CharacterInput('т'));
        recentText.Apply(TokenInputEvent.CharacterInput('е'));
        recentText.Apply(TokenInputEvent.CharacterInput('с'));
        recentText.Apply(TokenInputEvent.CharacterInput('т'));
        recentText.Apply(TokenInputEvent.Boundary(LiveCorrectionTestHelpers.SpaceBoundaryKey()));

        var boundary = LiveCorrectionTestHelpers.PunctuationBoundaryKey(',');
        Assert.True(engine.CanApplyPunctuationCorrection(boundary));
    }

    private static AutomaticLayoutCorrectionEngine CreateEnabledEngine(
        LiveCorrectionTestHelpers.FakeReplacementService replacement,
        LiveCorrectionTestHelpers.FakeBoundaryDeliveryService delivery)
    {
        return LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledPunctuationSettings(),
            replacement,
            delivery);
    }
}
