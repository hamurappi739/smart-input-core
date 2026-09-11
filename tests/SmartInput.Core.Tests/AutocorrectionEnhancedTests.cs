using System.Diagnostics;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class AutocorrectionEnhancedTests
{
    private readonly AutocorrectionService _service = new();
    private readonly CompositeAutocorrectDictionary _starterDictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    [Theory]
    [InlineData("превет", "привет")]
    [InlineData("мирр", "мир")]
    public void Evaluate_RussianTypos_WithStarterLexicon(string typo, string expected)
    {
        var result = _service.Evaluate(typo, TypingLanguage.Russian, _starterDictionary);

        Assert.Equal(expected, result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Theory]
    [InlineData("Превет", "Привет")]
    [InlineData("ПРЕВЕТ", "ПРИВЕТ")]
    public void Evaluate_RussianTypos_PreserveCapitalization(string typo, string expected)
    {
        var result = _service.Evaluate(typo, TypingLanguage.Russian, _starterDictionary);

        Assert.Equal(expected, result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_CorrectRussianAllCaps_ReturnsNoChange()
    {
        var result = _service.Evaluate("ПРИВЕТ", TypingLanguage.Russian, _starterDictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Theory]
    [InlineData("helo", "hello")]
    [InlineData("teh", "the")]
    [InlineData("adn", "and")]
    [InlineData("recieve", "receive")]
    [InlineData("becuase", "because")]
    [InlineData("thier", "their")]
    public void Evaluate_EnglishCommonTypos_WithStarterLexicon(string typo, string expected)
    {
        var result = _service.Evaluate(typo, TypingLanguage.English, _starterDictionary);

        Assert.Equal(expected, result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Theory]
    [InlineData("работает")]
    [InlineData("работали")]
    [InlineData("красивыми")]
    [InlineData("собакой")]
    [InlineData("людьми")]
    public void Evaluate_ValidRussianWordForms_AreNotCorrected(string word)
    {
        Assert.True(RussianWordFormBloomFilter.MightContain(word));

        var result = _service.Evaluate(word, TypingLanguage.Russian, _starterDictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void Evaluate_CommonRussianPronoun_Menya_IsNotCorrected()
    {
        var result = _service.Evaluate("меня", TypingLanguage.Russian, _starterDictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Theory]
    [InlineData("гавно")]
    [InlineData("говно")]
    [InlineData("блядь")]
    [InlineData("сука")]
    [InlineData("пиздец")]
    public void Evaluate_CommonColloquialWords_AreNotCorrected(string word)
    {
        var result = _service.Evaluate(word, TypingLanguage.Russian, _starterDictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void Evaluate_RepeatedLetterTypo_Buduut_CorrectsToBudut()
    {
        var result = _service.Evaluate("будуут", TypingLanguage.Russian, _starterDictionary);

        Assert.Equal("будут", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("world")]
    [InlineData("because")]
    [InlineData("receive")]
    public void Evaluate_ValidEnglishWords_AreNotCorrected(string word)
    {
        var result = _service.Evaluate(word, TypingLanguage.English, _starterDictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Theory]
    [InlineData("Zyxtab")]
    [InlineData("Qwertium")]
    public void Evaluate_UnknownBrandLikeTokens_AreNotCorrected(string token)
    {
        var result = _service.Evaluate(token, TypingLanguage.English, _starterDictionary);

        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("user@example.com")]
    [InlineData(@"C:\Projects\file.txt")]
    [InlineData("snake_case")]
    [InlineData("kebab-case")]
    [InlineData("camelCase")]
    [InlineData("PascalCase")]
    [InlineData("Console.WriteLine()")]
    [InlineData("git status")]
    [InlineData("v1.2.3")]
    [InlineData("2026-09-02")]
    public void Evaluate_ProtectedTokens_ReturnNoChange(string token)
    {
        var result = _service.Evaluate(token, TypingLanguage.English, _starterDictionary);

        Assert.Equal(AutocorrectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void Evaluate_SingleCharacter_ReturnsWait()
    {
        var result = _service.Evaluate("z", TypingLanguage.English, _starterDictionary);

        Assert.Equal(AutocorrectionRecommendation.Wait, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void Evaluate_TwoCharacterToken_IsConservative()
    {
        var result = _service.Evaluate("he", TypingLanguage.English, _starterDictionary);

        Assert.NotEqual(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_UserDictionaryCandidate_IsPreferredForTypo()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.English, "myword", 1.0, isUserEntry: true);

        var result = _service.Evaluate("mywrod", TypingLanguage.English, dictionary);

        Assert.Equal("myword", result.CandidateToken);
        Assert.Equal(AutocorrectionRecommendation.Candidate, result.Recommendation);
    }

    [Fact]
    public void Evaluate_AmbiguousTopCandidates_ReturnsWait()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.English, "car", 0.90);
        dictionary.Add(TypingLanguage.English, "arc", 0.90);

        var result = _service.Evaluate(
            "acr",
            TypingLanguage.English,
            dictionary,
            new AutocorrectionOptions
            {
                AmbiguousCandidateScoreGap = 1.0,
            });

        Assert.Equal(AutocorrectionRecommendation.Wait, result.Recommendation);
        Assert.Null(result.CandidateToken);
    }

    [Fact]
    public void Evaluate_IsDeterministicAcrossRuns()
    {
        var first = _service.Evaluate("helo", TypingLanguage.English, _starterDictionary);
        var second = _service.Evaluate("helo", TypingLanguage.English, _starterDictionary);

        Assert.Equal(first.CandidateToken, second.CandidateToken);
        Assert.Equal(first.ConfidenceScore, second.ConfidenceScore);
        Assert.Equal(first.Recommendation, second.Recommendation);
    }

    [Fact]
    public void Generate_RespectsCandidateCap()
    {
        var options = new AutocorrectionOptions
        {
            MaxGeneratedCandidates = 24,
            MaxSubstitutionTokenLength = 0,
        };

        var candidates = AutocorrectionCandidateGenerator.Generate(
            "abcdefghij",
            TypingLanguage.English,
            options);

        Assert.True(candidates.Count <= options.MaxGeneratedCandidates);
    }

    [Fact]
    public void Generate_CompletesWithinReasonableTime()
    {
        var options = new AutocorrectionOptions
        {
            MaxGeneratedCandidates = 512,
        };

        var stopwatch = Stopwatch.StartNew();
        var candidates = AutocorrectionCandidateGenerator.Generate(
            "abcdefghijklmnopqrstuvwxyz",
            TypingLanguage.English,
            options);
        stopwatch.Stop();

        Assert.NotEmpty(candidates);
        Assert.True(stopwatch.ElapsedMilliseconds < 250);
    }

    [Fact]
    public void CorrectionNotificationMessages_DoNotContainSampleTokens()
    {
        Assert.DoesNotContain("helo", CorrectionNotificationMessages.Autocorrect);
        Assert.DoesNotContain("hello", CorrectionNotificationMessages.Autocorrect);
        Assert.DoesNotContain("привет", CorrectionNotificationMessages.Layout);
    }

    [Fact]
    public async Task ProcessInputAsync_LayoutHasPriorityOverAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "ghbdtn");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("привет", replacement.LastReplacement);
        Assert.Equal(0, engine.Status.AutocorrectAttempts);
    }

    [Fact]
    public async Task ProcessInputAsync_OnlyOneReplacementPerBoundary()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(1, engine.Status.AutocorrectAttempts);
        Assert.Equal(0, engine.Status.CorrectionsAttempted);
    }

    [Fact]
    public async Task ProcessInputAsync_BoundaryDeliveredAfterAutocorrect()
    {
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            delivery);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57)));

        Assert.Equal(1, delivery.DeliverCallCount);
        Assert.Equal("hello", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_AutocorrectDisabled_SkipsCorrection()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledLayoutSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, engine.Status.AutocorrectAttempts);
    }

    [Fact]
    public async Task ProcessInputAsync_SecureInputPolicy_SkipsAutocorrect()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.Policy(AutomationPolicyState.SecureInput, allowsAutomation: false),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
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
    public async Task ProcessInputAsync_AfterTwoAutocorrectRejections_SkipsPair()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "helo",
            Replacement = "hello",
            Kind = CorrectionKind.Autocorrect,
            UndoCount = 2,
        });

        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            rejectionPolicy: new CorrectionRejectionPolicy(store));

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.True(engine.Status.AutocorrectBlocked > 0);
    }

    [Fact]
    public async Task ProcessInputAsync_AllowAgain_RestoresAutocorrect()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "helo",
            Replacement = "hello",
            Kind = CorrectionKind.Autocorrect,
            UndoCount = 2,
        });

        var policy = new CorrectionRejectionPolicy(store);
        await policy.AllowAgainAsync("helo", "hello", CorrectionKind.Autocorrect);

        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            rejectionPolicy: policy);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("hello", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_SuccessfulAutocorrect_NotifiesWithoutTokenText()
    {
        var notifier = new RecordingAutocorrectFeedbackNotifier();
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement,
            feedbackNotifier: notifier);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "helo");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(CorrectionKind.Autocorrect, notifier.LastSuccessfulKind);
        Assert.Equal(1, notifier.SuccessfulCorrectionCount);
    }

    [Fact]
    public async Task ManualLayoutConversion_RemainsAvailableWhenAutocorrectSuppressed()
    {
        var store = new InMemoryCorrectionRejectionLearningStore();
        await store.RecordRejectionAsync(new CorrectionRejectionLearningEntry
        {
            Candidate = "helo",
            Replacement = "hello",
            Kind = CorrectionKind.Autocorrect,
            UndoCount = 2,
        });

        var manualService = new ManualLayoutConversionService(
            new KeyboardLayoutConverter(),
            new ManualConversionSelectedTextService("helo"),
            new LiveCorrectionTestHelpers.FakeAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy()));

        var result = await manualService.ConvertSelectedTextAsync(LayoutConversionDirection.EnglishToRussian);

        Assert.Equal(LayoutConversionStatus.Success, result.Status);
    }

    [Fact]
    public async Task ProcessInputAsync_LayoutServiceWord_IsNotAutocorrected()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(layoutEnabled: false),
            replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "z");
        await engine.ProcessInputAsync(TokenInputEvent.Boundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(0, engine.Status.AutocorrectAttempts);
    }

    private sealed class ManualConversionSelectedTextService(string selectedText)
        : Platform.Abstractions.Text.ISelectedTextService
    {
        public Task<string?> GetSelectedTextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(selectedText);

        public Task<bool> ReplaceSelectedTextAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class RecordingAutocorrectFeedbackNotifier : ICorrectionFeedbackNotifier
    {
        public int SuccessfulCorrectionCount { get; private set; }

        public CorrectionKind? LastSuccessfulKind { get; private set; }

        public void NotifySuccessfulCorrection(CorrectionKind kind)
        {
            SuccessfulCorrectionCount++;
            LastSuccessfulKind = kind;
        }

        public void NotifyCorrectionUndone()
        {
        }

        public void NotifyUserInput()
        {
        }

        public void NotifyContextInvalidated()
        {
        }
    }
}
