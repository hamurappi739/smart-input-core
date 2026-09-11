using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Persistence;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class JointCorrectionPipelineTests
{
    private static TokenInputEvent SpaceBoundary()
    {
        return TokenInputEvent.Boundary(
            DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57));
    }

    [Theory]
    [InlineData("деелай", "делай")]
    [InlineData("машиина", "машина")]
    [InlineData("говноо", "говно")]
    [InlineData("меняяй", "меняй")]
    [InlineData("дериись", "дерись")]
    [InlineData("пениис", "пенис")]
    [InlineData("будуут", "будут")]
    [InlineData("напесал", "написал")]
    [InlineData("исслидование", "исследование")]
    [InlineData("посянить", "пояснить")]
    [InlineData("спецеально", "специально")]
    [InlineData("испровляет", "исправляет")]
    [InlineData("обстаят", "обстоят")]
    [InlineData("допалнение", "дополнение")]
    [InlineData("жызнь", "жизнь")]
    [InlineData("машына", "машина")]
    [InlineData("првет", "привет")]
    [InlineData("стрвнно", "странно")]
    [InlineData("кмнда", "команда")]
    [InlineData("здрвствуйте", "здравствуйте")]
    [InlineData("ришения", "решения")]
    [InlineData("дууш", "душ")]
    public async Task ProcessInputAsync_RussianTypos_CorrectAtBoundary(string typo, string expected)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = CreateAutocorrectEngine(replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, typo);
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(typo, replacement.LastOriginal);
        Assert.Equal(expected, replacement.LastReplacement);
    }

    [Theory]
    [InlineData("gtie", "пишу")]
    [InlineData("ghbdtn", "привет")]
    [InlineData("руддщ", "hello")]
    [InlineData("ррудщ", "hello")]
    [InlineData("вуфк", "dear")]
    [InlineData("рш", "hi")]
    [InlineData("цфше", "wait")]
    [InlineData("цщкл", "work")]
    [InlineData("dhjlt", "вроде")]
    [InlineData("dct", "все")]
    [InlineData("jrtq", "окей")]
    [InlineData("ybxtuj", "ничего")]
    [InlineData("мущ", "veo")]
    [InlineData("пзг", "gpu")]
    public async Task ProcessInputAsync_LayoutAndCombined_CorrectAtBoundary(string typo, string expected)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = CreateBothEngine(replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, typo);
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(typo, replacement.LastOriginal);
        Assert.Equal(expected, replacement.LastReplacement);
    }

    [Theory]
    [InlineData("вуфк", "dear")]
    [InlineData("рш", "hi")]
    [InlineData("цфше", "wait")]
    [InlineData("цщкл", "work")]
    public void JointDecision_CommonEnglishTypedOnRussianLayout_Applies(string typo, string expected)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        var detection = new WrongLayoutDetectionService(converter, dictionary).Evaluate(
            typo,
            ActiveLanguageSet.EnglishAndRussian);
        var guard = BoundedCandidateApplyGuard.Evaluate(
            typo,
            expected,
            CorrectionKind.Layout,
            TypingLanguage.Russian,
            dictionary,
            converter,
            new AutocorrectionOptions());
        var spelling = MutationClassificationOracle.Analyze(
            typo,
            TypingLanguage.Russian,
            dictionary,
            new AutocorrectionOptions());
        var result = CreateJointService().Evaluate(
            typo,
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true);

        Assert.True(
            result.Recommendation == JointCorrectionRecommendation.Apply
            && string.Equals(result.ReplacementToken, expected, StringComparison.OrdinalIgnoreCase),
            $"{typo}: decision={result.Recommendation} → {result.ReplacementToken}/{result.Kind}, "
            + $"detection={detection.Recommendation} → {detection.CandidateToken}, "
            + $"detectionScore={detection.ConfidenceScore:F2}, guard={guard}, "
            + $"exactRu={TrustedWordAnalyzer.IsExactKnownOriginal(typo, dictionary)}, "
            + $"enKnown={dictionary.Contains(expected, TypingLanguage.English)}, "
            + $"enFreq={dictionary.GetFrequency(expected, TypingLanguage.English):F2}, "
            + $"spelling={spelling.Class} → {spelling.UniqueTarget}");
    }

    [Theory]
    [InlineData("dhjlt", "вроде")]
    [InlineData("dct", "все")]
    [InlineData("jrtq", "окей")]
    public void JointDecision_CommonRussianTypedOnEnglishLayout_Applies(string typo, string expected)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var converter = new KeyboardLayoutConverter();
        var detection = new WrongLayoutDetectionService(converter, dictionary).Evaluate(
            typo,
            ActiveLanguageSet.EnglishAndRussian);
        var guard = BoundedCandidateApplyGuard.Evaluate(
            typo,
            expected,
            CorrectionKind.Layout,
            TypingLanguage.English,
            dictionary,
            converter,
            new AutocorrectionOptions());
        var spelling = MutationClassificationOracle.Analyze(
            typo,
            TypingLanguage.English,
            dictionary,
            new AutocorrectionOptions());
        var result = CreateJointService().Evaluate(
            typo,
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true);

        Assert.True(
            result.Recommendation == JointCorrectionRecommendation.Apply
            && string.Equals(result.ReplacementToken, expected, StringComparison.OrdinalIgnoreCase),
            $"{typo}: decision={result.Recommendation} → {result.ReplacementToken}/{result.Kind}, "
            + $"detection={detection.Recommendation} → {detection.CandidateToken}, "
            + $"detectionScore={detection.ConfidenceScore:F2}, guard={guard}, "
            + $"exactEn={TrustedWordAnalyzer.IsExactKnownOriginal(typo, dictionary)}, "
            + $"ruKnown={dictionary.Contains(expected, TypingLanguage.Russian)}, "
            + $"ruFreq={dictionary.GetFrequency(expected, TypingLanguage.Russian):F2}, "
            + $"spelling={spelling.Class} → {spelling.UniqueTarget}");
    }

    [Theory]
    [InlineData("дал")]
    [InlineData("видел")]
    [InlineData("дела")]
    [InlineData("дело")]
    [InlineData("нас")]
    [InlineData("нам")]
    [InlineData("меня")]
    [InlineData("гавно")]
    [InlineData("говно")]
    [InlineData("душ")]
    [InlineData("пишу")]
    [InlineData("делай")]
    [InlineData("машина")]
    [InlineData("меняй")]
    [InlineData("дерись")]
    [InlineData("пенис")]
    [InlineData("будут")]
    [InlineData("написал")]
    [InlineData("исследование")]
    [InlineData("пояснить")]
    [InlineData("специально")]
    [InlineData("исправляет")]
    [InlineData("обстоят")]
    [InlineData("дополнение")]
    [InlineData("касса")]
    [InlineData("ванна")]
    [InlineData("группа")]
    [InlineData("класс")]
    public async Task ProcessInputAsync_CorrectRussianWords_AreNotChanged(string word)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = CreateBothEngine(replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, word);
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Theory]
    [InlineData("camelCase")]
    [InlineData("PascalCase")]
    [InlineData("SmartInput")]
    public async Task ProcessInputAsync_ProtectedTokens_AreNotChanged(string token)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = CreateBothEngine(replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, token);
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
    }

    [Fact]
    public async Task ProcessInputAsync_ShortAmbiguousTokenDoesNotChooseWrongWord()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = CreateAutocorrectEngine(replacement);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "дла");
        await engine.ProcessInputAsync(SpaceBoundary());

        // Without sentence context, `дла` can map to several valid Russian
        // words. Waiting is safer than silently inserting `дал`.
        Assert.Equal(0, replacement.CallCount);
    }

    [Theory]
    [InlineData("будуут", "будут")]
    [InlineData("деелай", "делай")]
    [InlineData("дууш", "душ")]
    [InlineData("gtie", "пишу")]
    [InlineData("ghbdtn", "привет")]
    [InlineData("мущ", "veo")]
    public async Task ProcessInputAsync_DoubleShiftUndo_RestoresOriginalWithSpace(
        string typo,
        string corrected)
    {
        var replacement = new TrackingReplacementService();
        var undoService = new CorrectionUndoService(
            replacement,
            new LiveCorrectionTestHelpers.FakeAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy()),
            new LiveCorrectionTestHelpers.FakeSettingsService(LiveCorrectionTestHelpers.EnabledBothCorrectionSettings()),
            new InMemoryCorrectionRejectionLearningStore());
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement,
            undoService: undoService);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, typo);
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(corrected, replacement.LastReplacement);

        var undoResult = await undoService.TryUndoAsync();

        Assert.Equal(CorrectionUndoOutcome.Success, undoResult.Outcome);
        Assert.Equal($"{corrected} ", replacement.LastOriginal);
        Assert.Equal($"{typo} ", replacement.LastReplacement);
    }

    [Fact]
    public async Task ProcessInputAsync_EnglishLayoutCommandPhrase_CorrectsEachWord()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = CreateBothEngine(replacement);

        foreach (var token in new[] { "lfq", "rjvfyle", "pfgecrf" })
        {
            await LiveCorrectionTestHelpers.TypeTokenAsync(engine, token);
            await engine.ProcessInputAsync(SpaceBoundary());
        }

        Assert.Equal(3, replacement.CallCount);
        Assert.Equal("pfgecrf", replacement.LastOriginal);
        Assert.Equal("запуска", replacement.LastReplacement);
    }

    [Theory]
    [InlineData("lfq", "дай")]
    [InlineData("rjvfyle", "команду")]
    [InlineData("pfgecrf", "запуска")]
    public void JointDecision_EnglishLayoutCommandWords_AreCandidates(string token, string expected)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var joint = CreateJointService();
        var result = joint.Evaluate(token, dictionary, true, true);
        var detection = new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary)
            .Evaluate(token, ActiveLanguageSet.EnglishAndRussian);
        var spelling = new AutocorrectionService().Evaluate(
            token, TypingLanguage.English, dictionary, new AutocorrectionOptions());
        Assert.True(result.Recommendation == JointCorrectionRecommendation.Apply
            && string.Equals(result.ReplacementToken, expected, StringComparison.OrdinalIgnoreCase),
            $"{token}: {result.Recommendation} -> {result.ReplacementToken}/{result.Kind}; "
            + $"layout={detection.Recommendation}:{detection.CandidateToken}:{detection.ConfidenceScore:F2}; "
            + $"spell={spelling.Recommendation}:{spelling.CandidateToken}:{spelling.ConfidenceScore:F2}");
    }

    [Theory]
    [InlineData("акщтеутв", "frontend")]
    [InlineData("ифслутв", "backend")]
    [InlineData("афые", "fast")]
    [InlineData("фзш", "api")]
    [InlineData("djj,ot", "вообще")]
    public void JointDecision_GeneralPhysicalLayoutWords_AreCandidates(
        string token,
        string expected)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var result = CreateJointService().Evaluate(
            token,
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true);

        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(expected, result.ReplacementToken);
        Assert.Equal(CorrectionKind.Layout, result.Kind);
    }

    [Fact]
    public void JointDecision_Gtie_SelectsCombinedSpellingCorrection()
    {
        var joint = CreateJointService();
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();

        var result = joint.Evaluate(
            "gtie",
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true);

        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(CorrectionKind.Combined, result.Kind);
        Assert.Equal("пишу", result.ReplacementToken);
    }

    [Fact]
    public void JointDecision_Duush_PrefersRussianSpellingOverLayout()
    {
        var joint = CreateJointService();
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();

        var result = joint.Evaluate(
            "дууш",
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true);

        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal("душ", result.ReplacementToken);
        Assert.Equal(CorrectionKind.Autocorrect, result.Kind);
    }

    [Theory]
    [InlineData("гавно")]
    [InlineData("меня")]
    [InlineData("дууш")]
    public void JointDecision_TrustedRussianWords_AreNotLayoutConverted(string word)
    {
        var joint = CreateJointService();
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();

        var result = joint.Evaluate(
            word,
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true);

        if (string.Equals(word, "дууш", StringComparison.Ordinal))
        {
            Assert.Equal("душ", result.ReplacementToken);
            return;
        }

        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    private static AutomaticLayoutCorrectionEngine CreateAutocorrectEngine(
        LiveCorrectionTestHelpers.FakeReplacementService replacement)
    {
        return LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledAutocorrectSettings(),
            replacement);
    }

    private static AutomaticLayoutCorrectionEngine CreateBothEngine(
        LiveCorrectionTestHelpers.FakeReplacementService replacement)
    {
        return LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement);
    }

    private static JointCorrectionDecisionService CreateJointService()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var layoutConverter = new KeyboardLayoutConverter();
        var autocorrectionService = new AutocorrectionService();
        var layoutDetection = new WrongLayoutDetectionService(layoutConverter, dictionary);

        return new JointCorrectionDecisionService(
            layoutDetection,
            autocorrectionService,
            layoutConverter);
    }

    private sealed class TrackingReplacementService : ISafeTextReplacementService
    {
        public int CallCount { get; private set; }

        public string? LastOriginal { get; private set; }

        public string? LastReplacement { get; private set; }

        public Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOriginal = originalText;
            LastReplacement = replacementText;
            return Task.FromResult(TextReplacementResult.Success(originalText.Length, replacementText.Length));
        }

        public Task<TextReplacementResult> RunAbcToXyzDemoAsync(CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync("abc", "xyz", cancellationToken);

        public Task<TextReplacementResult> InsertTextAsync(string text, CancellationToken cancellationToken = default)
            => ReplaceRecentTextAsync(string.Empty, text, cancellationToken);
    }
}
