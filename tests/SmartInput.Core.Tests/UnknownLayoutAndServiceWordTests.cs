using SmartInput.Core.Configuration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;

namespace SmartInput.Core.Tests;

public class UnknownLayoutAndServiceWordTests
{
    private static readonly ActiveLanguageSet BothLanguages = ActiveLanguageSet.EnglishAndRussian;

    [Fact]
    public void Evaluate_UnknownMush_SuggestsVeoWithoutDictionary()
    {
        var service = CreateDetectionService();

        var result = service.Evaluate("мущ", BothLanguages);

        Assert.Equal("veo", result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold);
        Assert.True(result.ConfidenceScore <= WrongLayoutDetectionOptions.UnknownLatinNameMaxConfidence);
    }

    [Fact]
    public async Task Engine_UnknownMush_CorrectsToVeoWithoutDictionary()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("мущ", replacement.LastOriginal);
        Assert.Equal("veo", replacement.LastReplacement);
    }

    [Fact]
    public async Task Engine_UnknownMushSpaceThree_PreservesTrailingNumber()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());
        await engine.ProcessInputAsync(TokenInputEvent.CharacterInput('3'));

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("veo", replacement.LastReplacement);
    }

    [Fact]
    public void Evaluate_ConfidentUnknownLatinName_IsCandidateWithoutDictionary()
    {
        var service = CreateDetectionService();
        var result = service.Evaluate("руддщ", BothLanguages);

        Assert.Equal("hello", result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_KnownRussianWord_IsNotChanged()
    {
        var service = CreateDetectionService();

        var result = service.Evaluate("привет", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.True(result.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("user@example.com")]
    [InlineData(@"C:\Projects\file.txt")]
    [InlineData("myVariableName")]
    [InlineData("helloмир")]
    public void Evaluate_ProtectedTokens_AreNotChanged(string token)
    {
        var service = CreateDetectionService();

        var result = service.Evaluate(token, BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Theory]
    [InlineData("z", "я")]
    [InlineData("ns", "ты")]
    [InlineData("jyf", "она")]
    public void Evaluate_ServiceWordWhitelist_SuggestsRussianReplacement(string latin, string russian)
    {
        var service = CreateDetectionService();

        var result = service.Evaluate(latin, BothLanguages);

        Assert.Equal(russian, result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.Equal(LayoutConversionDirection.EnglishToRussian, result.ConversionDirection);
    }

    [Fact]
    public async Task Engine_ServiceWordZ_CorrectsToYaAndPreservesSpace()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var undoService = new LiveCorrectionTestHelpers.FakeCorrectionUndoService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "z");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("z", replacement.LastOriginal);
        Assert.Equal("я", replacement.LastReplacement);
        Assert.Equal(" ", undoService.LastRecordedTransaction?.TrailingText);
    }

    [Fact]
    public async Task Engine_RuToEnCorrection_SwitchesInputLanguageToEnglish()
    {
        var languageService = new RecordingKeyboardInputLanguageService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            keyboardInputLanguageService: languageService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(KeyboardInputLanguage.English, languageService.LastRequestedLanguage);
    }

    [Fact]
    public async Task Engine_CanonicalRepeatedHelloAlias_CorrectsAndSwitchesToEnglish()
    {
        var languageService = new RecordingKeyboardInputLanguageService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            keyboardInputLanguageService: languageService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ррудщ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal("hello", replacement.LastReplacement);
        Assert.Equal(KeyboardInputLanguage.English, languageService.LastRequestedLanguage);
    }

    [Fact]
    public async Task Engine_ServiceWord_SwitchesInputLanguageToRussian()
    {
        var languageService = new RecordingKeyboardInputLanguageService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            keyboardInputLanguageService: languageService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ns");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(KeyboardInputLanguage.Russian, languageService.LastRequestedLanguage);
    }

    [Fact]
    public async Task Engine_DoubleShiftUndo_RestoresServiceWordCorrection()
    {
        var learningStore = new InMemoryCorrectionRejectionLearningStore();
        var replacement = new TrackingReplacementService();
        var undoService = new CorrectionUndoService(
            replacement,
            new LiveCorrectionTestHelpers.FakeAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy()),
            new LiveCorrectionTestHelpers.FakeSettingsService(LiveCorrectionTestHelpers.EnabledLayoutSettings()),
            learningStore);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ns");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        var result = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.Success, result.Outcome);
        Assert.Equal("ты ", replacement.LastOriginal);
        Assert.Equal("ns ", replacement.LastReplacement);
    }

    [Theory]
    [InlineData(AutomationPolicyState.SafeMode)]
    [InlineData(AutomationPolicyState.SecureInput)]
    [InlineData(AutomationPolicyState.UnknownContext)]
    [InlineData(AutomationPolicyState.BlockedApplication)]
    public async Task Engine_BlockedPolicies_DoNotCorrectUnknownNames(AutomationPolicyState state)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(state, allowsAutomation: false),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task Engine_ProtectionDisabled_DoesNotCorrectUnknownNames()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService();
        var settings = new AppSettings
        {
            IsEnabled = false,
            AutomaticLayoutEnabled = true,
        };
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            settings,
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "мущ");
        await engine.ProcessInputAsync(CreateSpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public void Evaluate_ConfidentGpuFromPzg_IsCandidateWhenHeuristicPasses()
    {
        var service = CreateDetectionService();
        var result = service.Evaluate("пзг", BothLanguages);

        Assert.Equal("gpu", result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_Ghbdtn_StillSuggestsPrivet()
    {
        var service = CreateDetectionService();

        var result = service.Evaluate("ghbdtn", BothLanguages);

        Assert.Equal("привет", result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_AmbiguousLatinThreeLetter_DoesNotAutoCorrectWithoutConfidence()
    {
        var service = CreateDetectionService();

        var result = service.Evaluate("abc", BothLanguages);

        Assert.NotEqual(LayoutDetectionRecommendation.Candidate, result.Recommendation);
    }

    private static WrongLayoutDetectionService CreateDetectionService()
    {
        return new WrongLayoutDetectionService(new KeyboardLayoutConverter());
    }

    private static TokenInputEvent CreateSpaceBoundary()
    {
        return TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57));
    }

    private sealed class RecordingKeyboardInputLanguageService : IKeyboardInputLanguageService
    {
        public KeyboardInputLanguage? LastRequestedLanguage { get; private set; }

        public Task<bool> SetForegroundInputLanguageAsync(
            KeyboardInputLanguage language,
            CancellationToken cancellationToken = default)
        {
            LastRequestedLanguage = language;
            return Task.FromResult(true);
        }
    }

    private sealed class TrackingReplacementService : ISafeTextReplacementService
    {
        public string? LastOriginal { get; private set; }

        public string? LastReplacement { get; private set; }

        public Task<Platform.Abstractions.Text.TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            LastOriginal = originalText;
            LastReplacement = replacementText;
            return Task.FromResult(Platform.Abstractions.Text.TextReplacementResult.Success(
                originalText.Length,
                replacementText.Length));
        }

        public Task<Platform.Abstractions.Text.TextReplacementResult> RunAbcToXyzDemoAsync(
            CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync("abc", "xyz", cancellationToken);

        public Task<Platform.Abstractions.Text.TextReplacementResult> InsertTextAsync(
            string text,
            CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
    }
}
