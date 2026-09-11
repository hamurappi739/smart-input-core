using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class LiveSnippetExpansionTests
{
    [Fact]
    public async Task ProcessInputAsync_SlashAddr_ExpandsAtBoundary()
    {
        var (engine, replacement, delivery) = await CreateEnabledEngineAsync(
            CreateSnippet("/addr", "123 Main Street"));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "/addr");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("/addr", replacement.LastOriginal);
        Assert.Equal("123 Main Street", replacement.LastReplacement);
        Assert.Equal(1, engine.Status.SnippetExpansionsSucceeded);
        Assert.Equal(1, delivery.DeliverCallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_AtAtMail_Expands()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("@@mail", "user@example.com"));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "@@mail");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal("@@mail", replacement.LastOriginal);
        Assert.Equal("user@example.com", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_CyrillicTrigger_Expands()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("спс", "спасибо"));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "спс");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal("спс", replacement.LastOriginal);
        Assert.Equal("спасибо", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_CaseSensitiveTrigger_Honored()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("Sig", "Signature", caseSensitive: true));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(0, replacement.CallCount);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "Sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("Signature", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_CaseInsensitiveTrigger_Matches()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("sig", "Hello", caseSensitive: false));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "SIG");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal("Hello", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_LanguageFilter_Applied()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            new SnippetDefinition
            {
                Trigger = "addr",
                Replacement = "EN-only",
                Language = TypingLanguage.English,
                IsEnabled = true,
            });

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "addr");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(1, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_ApplicationFilter_Applied()
    {
        var context = new CorrectionApplicationContext();
        context.Update("notepad", 42);
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            new SnippetDefinition
            {
                Trigger = "onlyNote",
                Replacement = "ok",
                IsEnabled = true,
                EnabledApplications = ["notepad"],
            },
            applicationContext: context);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "onlyNote");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(1, replacement.CallCount);

        context.Update("word", 43);
        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "onlyNote");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(1, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_DisabledSnippet_NoExpansion()
    {
        var snippets = LiveCorrectionTestHelpers.CreateEmptySnippetService();
        await snippets.AddAsync(CreateSnippet("sig", "Hello", isEnabled: false));
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            EnabledSnippetSettings(),
            replacement,
            snippetService: snippets);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_SnippetsEnabledFalse_NoExpansion()
    {
        var snippets = LiveCorrectionTestHelpers.CreateEmptySnippetService();
        await snippets.AddAsync(CreateSnippet("sig", "Hello"));
        var settings = EnabledSnippetSettings();
        settings.SnippetsEnabled = false;
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            settings,
            replacement,
            snippetService: snippets);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_NoMatch_LeavesInputUnchanged()
    {
        var (engine, replacement, delivery) = await CreateEnabledEngineAsync(
            CreateSnippet("/addr", "expanded"));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "/other");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal(0, engine.Status.SnippetExpansionsSucceeded);
        Assert.True(engine.Status.SnippetExpansionsBlocked >= 1);
    }

    [Fact]
    public async Task ProcessInputAsync_SnippetHasPriorityOverLayout()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("ghbdtn", "SNIPPET"),
            layoutEnabled: true);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("SNIPPET", replacement.LastReplacement);
        Assert.Equal(1, engine.Status.SnippetExpansionsSucceeded);
        Assert.Equal(0, engine.Status.CorrectionsSucceeded);
    }

    [Fact]
    public async Task ProcessInputAsync_NoSnippetMatch_FallsThroughToLayout()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("/addr", "x"),
            layoutEnabled: true);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("привет", replacement.LastReplacement);
        Assert.Equal(1, engine.Status.CorrectionsSucceeded);
        Assert.Equal(0, engine.Status.SnippetExpansionsSucceeded);
    }

    [Theory]
    [InlineData(VirtualKeys.Space)]
    [InlineData(VirtualKeys.Tab)]
    [InlineData(VirtualKeys.Return)]
    public async Task ProcessInputAsync_BoundaryKinds_DeliverOnce(int virtualKey)
    {
        var (engine, replacement, delivery) = await CreateEnabledEngineAsync(
            CreateSnippet("sig", "Hello"));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(virtualKey, 0)));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(1, delivery.DeliverCallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_PunctuationBoundary_DeliversOnce()
    {
        var (engine, replacement, delivery) = await CreateEnabledEngineAsync(
            CreateSnippet("sig", "Hello"));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemPeriod, 0, '.')));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(1, delivery.DeliverCallCount);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    [InlineData(AutomationPolicyState.SafeMode)]
    public async Task ProcessInputAsync_BlockedPolicy_NoExpansion(AutomationPolicyState state)
    {
        var snippets = LiveCorrectionTestHelpers.CreateEmptySnippetService();
        await snippets.AddAsync(CreateSnippet("sig", "Hello"));
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(state, allowsAutomation: false),
            EnabledSnippetSettings(),
            replacement,
            snippetService: snippets);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_EmergencyPaused_NoExpansion()
    {
        var snippets = LiveCorrectionTestHelpers.CreateEmptySnippetService();
        await snippets.AddAsync(CreateSnippet("sig", "Hello"));
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            AutomationPolicyResult.EmergencyPaused(),
            EnabledSnippetSettings(),
            replacement,
            snippetService: snippets);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task NotifyApplicationContextChanged_ResetsSnippetBuffer()
    {
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("/addr", "x"));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "/ad");
        Assert.True(engine.PendingSnippetTriggerLength > 0);

        engine.NotifyApplicationContextChanged();
        Assert.Equal(0, engine.PendingSnippetTriggerLength);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "dr");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());
        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_Reset_ClearsSnippetBuffer()
    {
        var (engine, _, _) = await CreateEnabledEngineAsync(CreateSnippet("sig", "x"));
        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "si");
        await engine.ProcessInputAsync(TokenInputEvent.Reset);
        Assert.Equal(0, engine.PendingSnippetTriggerLength);
    }

    [Fact]
    public async Task ProcessInputAsync_UnsafeBackspace_ClearsSnippetBuffer()
    {
        var (engine, _, _) = await CreateEnabledEngineAsync(CreateSnippet("sig", "x"));
        await engine.ProcessInputAsync(TokenInputEvent.Backspace);
        Assert.Equal(0, engine.PendingSnippetTriggerLength);
    }

    [Fact]
    public async Task ProcessInputAsync_GenuineAbort_DeliversBoundaryOnce()
    {
        var snippets = LiveCorrectionTestHelpers.CreateEmptySnippetService();
        await snippets.AddAsync(CreateSnippet("sig", "Hello"));
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(
            (_, _) => TextReplacementResult.AbortedByUserInput(1, 0));
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            EnabledSnippetSettings(),
            replacement,
            delivery,
            snippetService: snippets);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "sig");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal(LiveLayoutCorrectionAction.SnippetFailed, engine.Status.LastAction);
    }

    [Fact]
    public async Task ProcessInputAsync_NoRecursiveExpansion()
    {
        var snippets = LiveCorrectionTestHelpers.CreateEmptySnippetService();
        await snippets.AddAsync(CreateSnippet("a", "b"));
        await snippets.AddAsync(CreateSnippet("b", "c"));
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            EnabledSnippetSettings(),
            replacement,
            snippetService: snippets);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "a");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("b", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_MultilineUnicodeReplacement()
    {
        var replacementText = "Привет,\n世界";
        var (engine, replacement, _) = await CreateEnabledEngineAsync(
            CreateSnippet("greet", replacementText));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "greet");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(replacementText, replacement.LastReplacement);
    }

    private static async Task<(
        AutomaticLayoutCorrectionEngine Engine,
        LiveCorrectionTestHelpers.FakeReplacementService Replacement,
        LiveCorrectionTestHelpers.FakeBoundaryDeliveryService Delivery)> CreateEnabledEngineAsync(
        SnippetDefinition snippet,
        bool layoutEnabled = false,
        ICorrectionApplicationContext? applicationContext = null)
    {
        var snippets = LiveCorrectionTestHelpers.CreateEmptySnippetService();
        await snippets.AddAsync(snippet);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var settings = EnabledSnippetSettings();
        settings.AutomaticLayoutEnabled = layoutEnabled;
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            settings,
            replacement,
            delivery,
            applicationContext: applicationContext,
            snippetService: snippets);
        return (engine, replacement, delivery);
    }

    private static AppSettings EnabledSnippetSettings()
    {
        return new AppSettings
        {
            IsEnabled = true,
            SnippetsEnabled = true,
            AutomaticLayoutEnabled = false,
            AutocorrectEnabled = false,
        };
    }

    private static SnippetDefinition CreateSnippet(
        string trigger,
        string replacement,
        bool caseSensitive = false,
        bool isEnabled = true)
    {
        return new SnippetDefinition
        {
            Trigger = trigger,
            Replacement = replacement,
            CaseSensitive = caseSensitive,
            IsEnabled = isEnabled,
        };
    }
}
