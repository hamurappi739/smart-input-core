using SmartInput.Core.Configuration;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;
using SmartInput.Platform.Windows.Input;
using SmartInput.Platform.Windows.Services;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.Core.Tests;

public class BoundaryOrderingTests
{
    [Fact]
    public async Task PreparedCorrection_ReplacesBeforeDeliveringOnlyThatBoundary()
    {
        var events = new List<string>();
        var replacement = new OrderingFakeReplacementService(success: true, events);
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(replacement, delivery);
        var space = DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0);

        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            space,
            new PreparedLayoutCorrection
            {
                OriginalText = "ghbdtn",
                ReplacementText = "привет",
            }));

        Assert.Equal("ghbdtn", replacement.LastOriginal);
        Assert.Equal("привет", replacement.LastReplacement);
        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(["replace", "deliver"], events);
    }

    [Fact]
    public async Task GhbdtnFollowedBySpace_ReplacementsBeforeBoundaryDelivery()
    {
        var events = new List<string>();
        var replacement = new OrderingFakeReplacementService(success: true, events);
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(replacement, delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        var space = DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(space));

        Assert.Equal("ghbdtn", replacement.LastOriginal);
        Assert.Equal("привет", replacement.LastReplacement);
        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(VirtualKeys.Space, delivery.DeliveredBoundaries[0].VirtualKeyCode);
        Assert.Equal(["replace", "deliver"], events);
        Assert.Equal(1, engine.Status.BoundariesDelivered);
    }

    [Fact]
    public async Task GhbdtnFollowedByTab_DeliversTabBoundary()
    {
        var delivery = new FakeBoundaryDeliveryService();
        var engine = BoundaryOrderingTestHelpers.CreateEngine(new OrderingFakeReplacementService(true), delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        var tab = DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Tab, 0);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(tab));

        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(VirtualKeys.Tab, delivery.DeliveredBoundaries[0].VirtualKeyCode);
    }

    [Fact]
    public async Task GhbdtnFollowedByEnter_DeliversEnterBoundary()
    {
        var delivery = new FakeBoundaryDeliveryService();
        var engine = BoundaryOrderingTestHelpers.CreateEngine(new OrderingFakeReplacementService(true), delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        var enter = DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Return, 0);
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(enter));

        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(VirtualKeys.Return, delivery.DeliveredBoundaries[0].VirtualKeyCode);
    }

    [Fact]
    public async Task GhbdtnFollowedByPunctuation_DeliversUnicodeBoundary()
    {
        var delivery = new FakeBoundaryDeliveryService();
        var engine = BoundaryOrderingTestHelpers.CreateEngine(new OrderingFakeReplacementService(true), delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        var comma = DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemComma, 0, ',');
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(comma));

        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(',', delivery.DeliveredBoundaries[0].Character);
        Assert.Equal(BoundaryDeliveryKind.UnicodeCharacter, delivery.DeliveredBoundaries[0].DeliveryKind);
    }

    [Fact]
    public async Task HelloFollowedBySpace_DeliversBoundaryWithoutReplacement()
    {
        var events = new List<string>();
        var replacement = new OrderingFakeReplacementService(true, events);
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(replacement, delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "hello");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Equal(["deliver"], events);
        Assert.Single(delivery.DeliveredBoundaries);
    }

    [Fact]
    public async Task WaitRecommendation_StillDeliversBoundaryOnce()
    {
        var events = new List<string>();
        var replacement = new OrderingFakeReplacementService(true, events);
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(replacement, delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "gh");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Equal(["deliver"], events);
        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(1, engine.Status.BoundariesDelivered);
    }

    [Fact]
    public async Task ReplacementAbort_StillDeliversBoundary()
    {
        var events = new List<string>();
        var replacement = new OrderingFakeReplacementService(success: false, events);
        var delivery = new FakeBoundaryDeliveryService(events);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(replacement, delivery);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Equal(["replace", "deliver"], events);
        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(LiveLayoutCorrectionAction.CorrectionFailed, engine.Status.LastAction);
    }

    [Fact]
    public async Task ReplacementTimeout_StillDeliversBoundary()
    {
        var replacement = new SlowReplacementService(delay: TimeSpan.FromSeconds(5));
        var delivery = new FakeBoundaryDeliveryService();
        var engine = BoundaryOrderingTestHelpers.CreateEngine(
            replacement,
            delivery,
            new LayoutCorrectionOptions { ReplacementTimeoutMilliseconds = 50 });

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(LiveLayoutCorrectionAction.CorrectionFailed, engine.Status.LastAction);
    }

    [Fact]
    public async Task SuppressedBoundary_IsCountedOnce()
    {
        var delivery = new FakeBoundaryDeliveryService();
        var engine = BoundaryOrderingTestHelpers.CreateEngine(new OrderingFakeReplacementService(true), delivery);
        var boundary = DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(boundary));

        Assert.Equal(1, engine.Status.BoundariesSuppressed);
        Assert.Equal(1, engine.Status.BoundariesDelivered);
        Assert.Single(delivery.DeliveredBoundaries);
    }

    [Fact]
    public async Task PolicyBlockedDuringCorrection_StillDeliversPreviouslySuppressedBoundary()
    {
        var delivery = new FakeBoundaryDeliveryService();
        var policySequence = new Queue<AutomationPolicyResult>([
            BoundaryOrderingTestHelpers.AllowedPolicy(),
            BoundaryOrderingTestHelpers.Policy(AutomationPolicyState.SafeMode, allowsAutomation: false),
        ]);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(
            new OrderingFakeReplacementService(true),
            delivery,
            policySequence: policySequence);

        await BoundaryOrderingTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Single(delivery.DeliveredBoundaries);
    }

    [Fact]
    public async Task RejectedPreparedCorrection_DeliversPreviouslySuppressedBoundaryOnce()
    {
        var delivery = new FakeBoundaryDeliveryService();
        var replacement = new OrderingFakeReplacementService(success: true);
        var engine = BoundaryOrderingTestHelpers.CreateEngine(replacement, delivery);

        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0),
            new PreparedLayoutCorrection
            {
                OriginalText = "ufdyj",
                ReplacementText = "гавно",
            }));

        Assert.Null(replacement.LastOriginal);
        Assert.Single(delivery.DeliveredBoundaries);
        Assert.Equal(1, engine.Status.BoundariesDelivered);
    }
}

public class LiveLayoutBoundaryGateTests
{
    [Fact]
    public void ShouldNotSuppressPendingBoundary_WhenOnlyTokenIsPending()
    {
        var gate = CreateGate(
            pendingLength: 6,
            protectionEnabled: true,
            automaticLayoutEnabled: true,
            emergencyPaused: false,
            policyState: AutomationPolicyState.Allowed,
            replacementActive: false);

        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ShouldNotSuppress_WhenTokenEmpty()
    {
        var gate = CreateGate(
            pendingLength: 0,
            protectionEnabled: true,
            automaticLayoutEnabled: true,
            emergencyPaused: false,
            policyState: AutomationPolicyState.Allowed,
            replacementActive: false);

        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ShouldNotSuppress_InSafeMode()
    {
        var gate = CreateGate(
            pendingLength: 6,
            protectionEnabled: true,
            automaticLayoutEnabled: true,
            emergencyPaused: false,
            policyState: AutomationPolicyState.SafeMode,
            replacementActive: false);

        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ShouldNotSuppress_WhenEmergencyPaused()
    {
        var gate = CreateGate(
            pendingLength: 6,
            protectionEnabled: true,
            automaticLayoutEnabled: true,
            emergencyPaused: true,
            policyState: AutomationPolicyState.Allowed,
            replacementActive: false);

        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ShouldNotSuppress_WhenAutomaticLayoutDisabled()
    {
        var gate = CreateGate(
            pendingLength: 6,
            protectionEnabled: true,
            automaticLayoutEnabled: false,
            emergencyPaused: false,
            policyState: AutomationPolicyState.Allowed,
            replacementActive: false);

        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ShouldNotSuppress_DuringReplacement()
    {
        var gate = CreateGate(
            pendingLength: 6,
            protectionEnabled: true,
            automaticLayoutEnabled: true,
            emergencyPaused: false,
            policyState: AutomationPolicyState.Allowed,
            replacementActive: true);

        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ObserveKeyDown_ConfidentWrongLayoutAtSpace_PreparesCorrection()
    {
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter()));

        foreach (var character in "ghbdtn")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        Thread.Sleep(100);
        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal("ghbdtn", correction.OriginalText);
        Assert.Equal("привет", correction.ReplacementText);
    }

    [Fact]
    public void ObserveKeyDown_Tab_DoesNotPrepareCorrection()
    {
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter()));

        foreach (var character in "ghbdtn")
        {
            gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character));
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Tab, 0)));

        Assert.Null(correction);
    }

    [Fact]
    public void ObserveKeyDown_AutocorrectOnly_RequestsBoundarySuppression()
    {
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings
            {
                IsEnabled = true,
                AutomaticLayoutEnabled = false,
                AutocorrectEnabled = true,
            }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter()));

        foreach (var character in "helo")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Null(correction);
        Assert.True(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ObserveKeyDown_KnownWordWithAutocorrect_DoesNotHoldBoundary()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings
            {
                IsEnabled = true,
                AutomaticLayoutEnabled = true,
                AutocorrectEnabled = true,
            }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        foreach (var character in "привет")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0))));
        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ObserveKeyDown_UnknownAutocorrectToken_StillHoldsBoundary()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings
            {
                IsEnabled = true,
                AutomaticLayoutEnabled = true,
                AutocorrectEnabled = true,
            }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        foreach (var character in "стрвнно")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0))));
        Assert.True(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ObserveKeyDown_EnglishCommaThenSAfterWhitespace_PreparesRussianServiceWord()
    {
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter()));

        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0))));
        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemComma, 51, ','))));
        Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('s')));

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal(",s", correction.OriginalText);
        Assert.Equal("бы", correction.ReplacementText);
        Assert.Equal(KeyboardInputLanguage.Russian, correction.TargetInputLanguage);
    }

    [Fact]
    public void ObserveKeyDown_InternalEnglishLayoutComma_PreparesWholeRussianWord()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        foreach (var character in "hf")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        // On the English layout the physical Russian б key produces a comma;
        // it is part of the word "работа", not a word boundary.
        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemComma, 51, ','))));
        Assert.False(gate.ShouldSuppressPendingBoundary());

        foreach (var character in "jnf")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal("hf,jnf", correction.OriginalText);
        Assert.Equal("работа", correction.ReplacementText);
        Assert.Equal(KeyboardInputLanguage.Russian, correction.TargetInputLanguage);
        Assert.True(BoundedCandidateApplyGuard.AllowsPreparedLayout(
            correction.OriginalText,
            correction.ReplacementText,
            dictionary,
            new KeyboardLayoutConverter()));
    }

    [Theory]
    [InlineData("акщтеутв", "frontend")]
    [InlineData("ифслутв", "backend")]
    [InlineData("афые", "fast")]
    [InlineData("фзш", "api")]
    [InlineData("djj,ot", "вообще")]
    public void ObserveKeyDown_GeneralPhysicalLayoutForms_PreparesMappedTarget(
        string typedToken,
        string expectedReplacement)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        foreach (var character in typedToken)
        {
            if (character == ',')
            {
                Assert.Null(gate.ObserveKeyDown(
                    KeyboardCharacterResolution.CreateBoundary(
                        DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemComma, 51, ','))));
            }
            else
            {
                Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
            }
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal(typedToken, correction.OriginalText);
        Assert.Equal(expectedReplacement, correction.ReplacementText);
    }

    [Theory]
    [InlineData("z", "я")]
    [InlineData("ш", "i")]
    [InlineData("ру", "he")]
    [InlineData("ьу", "me")]
    [InlineData("ьн", "my")]
    [InlineData("vs", "мы")]
    [InlineData("'nj", "это")]
    [InlineData("nen", "тут")]
    [InlineData("rfr", "как")]
    [InlineData("jyb", "они")]
    [InlineData("jyj", "оно")]
    [InlineData("t;br", "ежик")]
    [InlineData("rjn", "кот")]
    [InlineData("xvj", "чмо")]
    [InlineData("jgf", "опа")]
    [InlineData("ldf", "два")]
    [InlineData("nhb", "три")]
    [InlineData("[jnm", "хоть")]
    [InlineData("vtyz", "меня")]
    [InlineData("црщ", "who")]
    [InlineData("шеы", "its")]
    [InlineData("ше", "it")]
    [InlineData("дуфл", "leak")]
    [InlineData("сфк", "car")]
    [InlineData("рше", "hit")]
    [InlineData("рще", "hot")]
    [InlineData("рщу", "hoe")]
    [InlineData("рун", "hey")]
    [InlineData("ыру", "she")]
    public void ObserveKeyDown_ShortWordsFromOneToFour_PreparesExactLayout(
        string typedToken,
        string expectedReplacement)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        foreach (var character in typedToken)
        {
            var resolution = InputCharacterClassification.IsWordBoundaryCharacter(character)
                ? KeyboardCharacterResolution.CreateBoundary(
                    DeferredBoundaryKey.FromUnicodeCharacter(0, 0, character))
                : KeyboardCharacterResolution.CharacterOf(character);
            Assert.Null(gate.ObserveKeyDown(resolution));
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal(typedToken, correction.OriginalText);
        Assert.Equal(expectedReplacement, correction.ReplacementText);
    }

    [Fact]
    public void ObserveKeyDown_KnownRussianShortCollision_UsesPriorEnglishContext()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateProductionLikeDictionary();
        var context = new SentenceLanguageContextBuffer();
        context.RecordCompletedToken("she");
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary,
            sentenceLanguageContext: context);

        foreach (var character in "рук")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal("рук", correction.OriginalText);
        Assert.Equal("her", correction.ReplacementText);
    }

    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData('\'')]
    public void ObserveKeyDown_StandalonePunctuation_RemainsPunctuation(char character)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromUnicodeCharacter(0, 0, character))));
        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0))));
        Assert.False(gate.ShouldSuppressPendingBoundary());
    }

    [Fact]
    public void ObserveKeyDown_LeadingEnglishComma_PreparesWholeRussianWord()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0))));
        Assert.Null(gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromUnicodeCharacter(VirtualKeys.OemComma, 51, ','))));

        foreach (var character in "jkmijq")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal(",jkmijq", correction.OriginalText);
        Assert.Equal("большой", correction.ReplacementText);
    }

    [Fact]
    public void ObserveKeyDown_AfterContextReset_RebasesCurrentCharacterOnce()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        // The hook can observe the first character before the coordinator
        // notices a foreground change and resets the preflight gate.
        gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('g'));
        gate.ResetPendingToken();
        gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('g'));

        foreach (var character in "hbdtn")
        {
            Assert.Null(gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character)));
        }

        Thread.Sleep(100);
        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal("ghbdtn", correction.OriginalText);
        Assert.Equal("привет", correction.ReplacementText);
    }

    [Fact]
    public void ObserveKeyDown_ImmediateBoundary_UsesFastDictionaryLayoutCandidate()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings
            {
                IsEnabled = true,
                AutomaticLayoutEnabled = true,
            }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new ThrowingWrongLayoutDetectionService(),
            dictionary);

        foreach (var character in "ghbdtn")
        {
            gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character));
        }

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal("ghbdtn", correction!.OriginalText);
        Assert.Equal("привет", correction.ReplacementText);
        Assert.Equal(KeyboardInputLanguage.Russian, correction.TargetInputLanguage);
    }

    [Fact]
    public void ObserveApplicationContext_FocusChangeResetsBeforeNewTokenCharacters()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            dictionary);

        gate.ObserveApplicationContext((nint)1);
        foreach (var character in "ghbdtn")
        {
            gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character));
        }

        // This is the hook-time ordering guarantee: the old token is cleared
        // before any character from the newly focused window is appended.
        gate.ObserveApplicationContext((nint)2);
        foreach (var character in "ghbdtn")
        {
            gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character));
        }

        Thread.Sleep(100);
        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.NotNull(correction);
        Assert.Equal("ghbdtn", correction.OriginalText);
        Assert.Equal("привет", correction.ReplacementText);
    }

    [Fact]
    public void ObserveKeyDown_KnownSourceWord_SkipsSynchronousLayoutEvaluation()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var detection = new CountingWrongLayoutDetectionService();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            detection,
            dictionary);

        foreach (var character in "привет")
        {
            gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf(character));
        }

        gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Equal(0, detection.CallCount);
    }

    [Fact]
    public async Task PreflightResultCompletedAfterReset_IsDiscardedAsStale()
    {
        var detection = new BlockingWrongLayoutDetectionService();
        var gate = new LiveLayoutBoundaryGate(
            new FakeTokenState(0, AutomationPolicyState.Allowed),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            new FakeEmergencyPauseService(isPaused: false),
            new FakeReplacementSessionNotifier(isActive: false),
            detection);

        gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('g'));
        gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('h'));
        gate.ObserveKeyDown(KeyboardCharacterResolution.CharacterOf('b'));
        await detection.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        gate.ResetPendingToken();
        detection.Release.TrySetResult();
        await detection.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        var correction = gate.ObserveKeyDown(
            KeyboardCharacterResolution.CreateBoundary(
                DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)));

        Assert.Null(correction);
    }

    private sealed class CountingWrongLayoutDetectionService : IWrongLayoutDetectionService
    {
        public int CallCount { get; private set; }

        public WrongLayoutDetectionResult Evaluate(
            string token,
            ActiveLanguageSet activeLanguages,
            WrongLayoutDetectionOptions? options = null)
        {
            CallCount++;
            return new WrongLayoutDetectionResult
            {
                OriginalToken = token,
                Recommendation = LayoutDetectionRecommendation.NoChange,
            };
        }
    }

    private sealed class BlockingWrongLayoutDetectionService : IWrongLayoutDetectionService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WrongLayoutDetectionResult Evaluate(
            string token,
            ActiveLanguageSet activeLanguages,
            WrongLayoutDetectionOptions? options = null)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            Finished.TrySetResult();
            return new WrongLayoutDetectionResult
            {
                OriginalToken = token,
                CandidateToken = "candidate",
                ConversionDirection = LayoutConversionDirection.EnglishToRussian,
                ConfidenceScore = 0.99,
                Recommendation = LayoutDetectionRecommendation.Candidate,
            };
        }
    }

    private sealed class ThrowingWrongLayoutDetectionService : IWrongLayoutDetectionService
    {
        public WrongLayoutDetectionResult Evaluate(
            string token,
            ActiveLanguageSet activeLanguages,
            WrongLayoutDetectionOptions? options = null)
            => throw new InvalidOperationException(
                "The immediate boundary test must use the synchronous dictionary path.");
    }

    private static LiveLayoutBoundaryGate CreateGate(
        int pendingLength,
        bool protectionEnabled,
        bool automaticLayoutEnabled,
        bool emergencyPaused,
        AutomationPolicyState policyState,
        bool replacementActive)
    {
        return new LiveLayoutBoundaryGate(
            new FakeTokenState(pendingLength, policyState),
            new FakeSettingsService(new AppSettings
            {
                IsEnabled = protectionEnabled,
                AutomaticLayoutEnabled = automaticLayoutEnabled,
            }),
            new FakeEmergencyPauseService(emergencyPaused),
            new FakeReplacementSessionNotifier(replacementActive));
    }

    private sealed class FakeTokenState(int pendingLength, AutomationPolicyState policyState)
        : IAutomaticLayoutCorrectionEngine
    {
        public LiveLayoutCorrectionStatus Status { get; } = new();

        public int PendingTokenLength => pendingLength;

        public int PendingSnippetTriggerLength => 0;

        public bool HasPendingLiveWork => pendingLength > 0;

        public AutomationPolicyState CachedPolicyState => policyState;

        public Task ProcessInputAsync(TokenInputEvent input, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void ResetBuffer(string reason)
        {
        }

        public void NotifyApplicationContextChanged()
        {
        }

        public void NotifyPolicyContextChanged(AutomationPolicyResult policy)
        {
        }

        public bool CanApplyPunctuationCorrection(DeferredBoundaryKey boundary) => false;
    }

    private sealed class FakeEmergencyPauseService(bool isPaused) : Platform.Abstractions.Safety.IEmergencyPauseService
    {
        public bool IsPaused => isPaused;

        public void Pause()
        {
        }

        public void Resume()
        {
        }
    }

    private sealed class FakeReplacementSessionNotifier(bool isActive) : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => isActive;

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}

public class WindowsBoundaryKeyInterceptorTests
{
    [Fact]
    public void TryIntercept_InjectedEvent_PassesThroughWithoutSuppress()
    {
        var interceptor = CreateInterceptor(new BoundaryKeyPairingTracker());

        var observation = new KeyboardObservationEventArgs
        {
            VirtualKeyCode = VirtualKeys.Space,
            EventType = PlatformKeyEventType.KeyDown,
        };

        var metadata = new KeyboardHookMetadata(KeyboardHookFlags.Injected, 0);
        var result = interceptor.TryIntercept(observation, metadata);

        Assert.False(result.ShouldSuppress);
    }

    [Fact]
    public void TryIntercept_GenuineBoundaryWithPendingToken_Suppresses()
    {
        var boundary = DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0);
        var tracker = new BoundaryKeyPairingTracker();
        var interceptor = CreateInterceptor(tracker, boundary);

        var observation = new KeyboardObservationEventArgs
        {
            VirtualKeyCode = VirtualKeys.Space,
            EventType = PlatformKeyEventType.KeyDown,
        };

        var result = interceptor.TryIntercept(observation, new KeyboardHookMetadata(0, 0));

        Assert.True(result.ShouldSuppress);
        Assert.Equal(VirtualKeys.Space, result.Boundary?.VirtualKeyCode);
        Assert.Equal(BoundaryPairingState.AwaitingPhysicalKeyUp, tracker.State);
    }

    [Fact]
    public void TryIntercept_DuringReplacement_PassesThrough()
    {
        var interceptor = CreateInterceptor(
            new BoundaryKeyPairingTracker(),
            replacementActive: true);

        var observation = new KeyboardObservationEventArgs
        {
            VirtualKeyCode = VirtualKeys.Space,
            EventType = PlatformKeyEventType.KeyDown,
        };

        var result = interceptor.TryIntercept(observation, new KeyboardHookMetadata(0, 0));

        Assert.False(result.ShouldSuppress);
    }

    [Fact]
    public void TryIntercept_RecordsResolvedAndInterceptedStages()
    {
        var metrics = new PerformanceMetricsCollector();
        var interceptor = new WindowsBoundaryKeyInterceptor(
            new WindowsInputObservationFilter(),
            new FakeResolver(CharacterResolutionKind.WordBoundary),
            new AlwaysSuppressGate(),
            new FakeReplacementSessionNotifier(false),
            new BoundaryKeyPairingTracker(),
            metrics);

        var result = interceptor.TryIntercept(
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = VirtualKeys.Space,
                EventType = PlatformKeyEventType.KeyDown,
            },
            new KeyboardHookMetadata(0, 0));

        Assert.True(result.ShouldSuppress);
        Assert.Equal(1, metrics.GetSnapshot().LivePipeline.CharacterResolved);
        Assert.Equal(1, metrics.GetSnapshot().LivePipeline.BoundaryIntercepted);
    }

    [Fact]
    public void TryIntercept_ObservesForegroundContextBeforeAppendingKey()
    {
        var calls = new List<string>();
        var interceptor = new WindowsBoundaryKeyInterceptor(
            new WindowsInputObservationFilter(),
            new FakeResolver(CharacterResolutionKind.Character),
            new OrderingContextGate(calls),
            new FakeReplacementSessionNotifier(false),
            new BoundaryKeyPairingTracker());

        interceptor.TryIntercept(
            new KeyboardObservationEventArgs
            {
                VirtualKeyCode = 0x41,
                EventType = PlatformKeyEventType.KeyDown,
                ForegroundWindowHandle = (nint)42,
            },
            new KeyboardHookMetadata(0, 0));

        Assert.Equal(["context", "key"], calls);
    }

    private static WindowsBoundaryKeyInterceptor CreateInterceptor(
        BoundaryKeyPairingTracker tracker,
        DeferredBoundaryKey? boundary = null,
        bool replacementActive = false)
    {
        return new WindowsBoundaryKeyInterceptor(
            new WindowsInputObservationFilter(),
            new FakeResolver(CharacterResolutionKind.WordBoundary, boundary),
            new AlwaysSuppressGate(),
            new FakeReplacementSessionNotifier(replacementActive),
            tracker);
    }

    private sealed class FakeResolver(CharacterResolutionKind kind, DeferredBoundaryKey? boundary = null)
        : IKeyboardCharacterResolver
    {
        public KeyboardCharacterResolution Resolve(KeyboardObservationEventArgs observation)
        {
            return kind switch
            {
                CharacterResolutionKind.WordBoundary when boundary is not null =>
                    KeyboardCharacterResolution.CreateBoundary(boundary),
                CharacterResolutionKind.WordBoundary =>
                    KeyboardCharacterResolution.CreateBoundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 0)),
                _ => KeyboardCharacterResolution.Ignored(),
            };
        }
    }

    private sealed class AlwaysSuppressGate : ILiveLayoutBoundaryGate
    {
        public bool ShouldSuppressPendingBoundary() => true;

        public PreparedLayoutCorrection? ObserveKeyDown(KeyboardCharacterResolution resolution)
        {
            return resolution.Kind == CharacterResolutionKind.WordBoundary
                ? new PreparedLayoutCorrection { OriginalText = "ghbdtn", ReplacementText = "привет" }
                : null;
        }
    }

    private sealed class OrderingContextGate(List<string> calls) : ILiveLayoutBoundaryGate
    {
        public bool ShouldSuppressPendingBoundary() => false;

        public void ObserveApplicationContext(nint windowHandle)
        {
            calls.Add("context");
        }

        public PreparedLayoutCorrection? ObserveKeyDown(KeyboardCharacterResolution resolution)
        {
            calls.Add("key");
            return null;
        }
    }

    private sealed class FakeReplacementSessionNotifier(bool isActive) : ITextReplacementSessionNotifier
    {
        public bool IsReplacementActive => isActive;

        public void NotifyKeyboardEventDuringReplacement(
            PlatformKeyEventType eventType,
            KeyboardHookMetadata metadata)
        {
        }
    }
}

internal static class BoundaryOrderingTestHelpers
{
    internal static AutomaticLayoutCorrectionEngine CreateEngine(
        ISafeTextReplacementService replacement,
        FakeBoundaryDeliveryService delivery,
        LayoutCorrectionOptions? options = null,
        Queue<AutomationPolicyResult>? policySequence = null)
    {
        return new AutomaticLayoutCorrectionEngine(
            new WrongLayoutDetectionService(new KeyboardLayoutConverter()),
            new AutocorrectionService(),
            LiveCorrectionTestHelpers.CreateStarterDictionary(),
            LiveCorrectionTestHelpers.CreateEmptySnippetService(),
            replacement,
            new SequencedAutomationSafetyService(policySequence ?? new Queue<AutomationPolicyResult>([AllowedPolicy()])),
            new FakeSettingsService(new AppSettings { IsEnabled = true, AutomaticLayoutEnabled = true }),
            delivery,
            new LiveCorrectionTestHelpers.FakeCorrectionUndoService(),
            new CorrectionApplicationContext(),
            NullPerformanceMetricsRecorder.Instance,
            options);
    }

    internal static async Task TypeTokenAsync(IAutomaticLayoutCorrectionEngine engine, string token)
    {
        foreach (var character in token)
        {
            await engine.ProcessInputAsync(TokenInputEvent.CharacterInput(character));
        }
    }

    internal static AutomationPolicyResult AllowedPolicy()
    {
        return Policy(AutomationPolicyState.Allowed, allowsAutomation: true);
    }

    internal static AutomationPolicyResult Policy(AutomationPolicyState state, bool allowsAutomation)
    {
        return new AutomationPolicyResult
        {
            State = state,
            AllowsAutomation = allowsAutomation,
            AllowsManualExternalTextOperations = state != AutomationPolicyState.SecureInput,
        };
    }
}

internal sealed class FakeBoundaryDeliveryService : IBoundaryKeyDeliveryService
{
    private readonly List<string>? _events;

    public FakeBoundaryDeliveryService(List<string>? events = null)
    {
        _events = events;
    }

    public List<DeferredBoundaryKey> DeliveredBoundaries { get; } = [];

    public int DeliverCallCount { get; private set; }

    public Task DeliverAsync(DeferredBoundaryKey boundary, CancellationToken cancellationToken = default)
    {
        DeliverCallCount++;
        _events?.Add("deliver");
        DeliveredBoundaries.Add(boundary);
        return Task.CompletedTask;
    }
}

internal sealed class OrderingFakeReplacementService(bool success, List<string>? events = null) : ISafeTextReplacementService
{
    public string? LastOriginal { get; private set; }

    public string? LastReplacement { get; private set; }

    public Task<TextReplacementResult> ReplaceRecentTextAsync(
        string originalText,
        string replacementText,
        CancellationToken cancellationToken = default)
    {
        events?.Add("replace");
        LastOriginal = originalText;
        LastReplacement = replacementText;

        return Task.FromResult(success
            ? TextReplacementResult.Success(originalText.Length, replacementText.Length)
            : TextReplacementResult.AbortedByUserInput(originalText.Length, replacementText.Length));
    }

    public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
    {
        return ReplaceRecentTextAsync("abc", "xyz", cancellationToken);
    }

    public Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
    {
        return ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
    }
}

internal sealed class SlowReplacementService(TimeSpan delay) : ISafeTextReplacementService
{
    public async Task<TextReplacementResult> ReplaceRecentTextAsync(
        string originalText,
        string replacementText,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        return TextReplacementResult.Success(originalText.Length, replacementText.Length);
    }

    public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
    {
        return ReplaceRecentTextAsync("abc", "xyz", cancellationToken);
    }

    public async Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
    {
        return await ReplaceRecentTextAsync(string.Empty, text, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SequencedAutomationSafetyService(Queue<AutomationPolicyResult> policies)
    : IAutomationSafetyService
{
    public AutomationPolicyResult EvaluateCurrentContext()
    {
        return policies.Count > 1 ? policies.Dequeue() : policies.Peek();
    }

    public Task<AutomationPolicyResult> EvaluateCurrentContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(EvaluateCurrentContext());
    }

    public bool IsOperationAllowed(AutomationOperationKind operationKind) =>
        EvaluateCurrentContext().IsAllowed(operationKind);

    public Task<bool> IsOperationAllowedAsync(
        AutomationOperationKind operationKind,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(IsOperationAllowed(operationKind));
    }

    public string? GetBlockedReason(AutomationOperationKind operationKind)
    {
        var policy = EvaluateCurrentContext();
        return policy.IsAllowed(operationKind) ? null : policy.Reason;
    }
}

internal sealed class FakeSettingsService(AppSettings settings) : ISettingsService
{
    public AppSettings Current { get; private set; } = settings;

        public event Action? SettingsChanged;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
    {
        update(Current);
        return Task.CompletedTask;
    }
}
