using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

public class ExactWordProtectionAndRegressionTests
{
    private static TokenInputEvent SpaceBoundary() =>
        TokenInputEvent.Boundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57));

    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly AutocorrectionService _autocorrection = new();
    private readonly JointCorrectionDecisionService _joint;

    public ExactWordProtectionAndRegressionTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            _autocorrection,
            converter);
    }

    [Fact]
    public void Minyay_CorrectsToMenyay_NotLatin()
    {
        var joint = _joint.Evaluate("миняй", _dictionary, true, true);

        Assert.Equal(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal("меняй", joint.ReplacementToken);
        Assert.Equal(CorrectionKind.Autocorrect, joint.Kind);
    }

    [Fact]
    public void Vbyzq_CombinedCorrectsToMenyay()
    {
        var joint = _joint.Evaluate("vbyzq", _dictionary, true, true);

        Assert.Equal(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal("меняй", joint.ReplacementToken);
        Assert.Equal(CorrectionKind.Combined, joint.Kind);
    }

    [Theory]
    [InlineData("неизвестное")]
    [InlineData("неизвестно")]
    [InlineData("дать")]
    [InlineData("жать")]
    [InlineData("дела")]
    [InlineData("дело")]
    [InlineData("нас")]
    [InlineData("нам")]
    [InlineData("меня")]
    [InlineData("сеня")]
    [InlineData("видел")]
    [InlineData("видик")]
    [InlineData("дал")]
    [InlineData("лал")]
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
    [InlineData("стать")]
    [InlineData("знать")]
    [InlineData("пить")]
    [InlineData("жить")]
    [InlineData("бить")]
    [InlineData("быть")]
    [InlineData("том")]
    [InlineData("дом")]
    [InlineData("код")]
    [InlineData("кот")]
    [InlineData("неизвестный")]
    [InlineData("неизвестная")]
    [InlineData("неизвестные")]
    [InlineData("делу")]
    [InlineData("делом")]
    [InlineData("деле")]
    [InlineData("видеть")]
    [InlineData("вижу")]
    [InlineData("видишь")]
    [InlineData("видит")]
    [InlineData("видела")]
    [InlineData("видели")]
    [InlineData("мы")]
    [InlineData("нами")]
    [InlineData("мной")]
    [InlineData("дам")]
    [InlineData("дашь")]
    [InlineData("даст")]
    [InlineData("дадим")]
    [InlineData("дадите")]
    [InlineData("дадут")]
    [InlineData("дала")]
    [InlineData("дали")]
    public void ExactKnownRussianWords_AreNotChanged(string word)
    {
        var joint = _joint.Evaluate(word, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, joint.Recommendation);
    }

    [Theory]
    [InlineData("миняй", "меняй")]
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
    [InlineData("дууш", "душ")]
    public void RussianTypos_AreCorrected(string typo, string expected)
    {
        var joint = _joint.Evaluate(typo, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal(expected, joint.ReplacementToken);
    }

    [Theory]
    [InlineData("gtie", "пишу")]
    [InlineData("ghbdtn", "привет")]
    [InlineData("руддщ", "hello")]
    [InlineData("мущ", "veo")]
    [InlineData("пзг", "gpu")]
    [InlineData("vbyzq", "меняй")]
    public void LayoutAndCombinedCases_AreCorrected(string typo, string expected)
    {
        var joint = _joint.Evaluate(typo, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal(expected, joint.ReplacementToken);
    }

    [Theory]
    [InlineData("касса")]
    [InlineData("ванна")]
    [InlineData("группа")]
    [InlineData("класс")]
    [InlineData("суббота")]
    [InlineData("россия")]
    [InlineData("алла")]
    [InlineData("hello")]
    [InlineData("letter")]
    [InlineData("coffee")]
    [InlineData("class")]
    public void LegitimateDoubleLetters_AreNotChanged(string word)
    {
        var language = TokenScriptAnalyzer.Classify(word) == TokenScript.Cyrillic
            ? TypingLanguage.Russian
            : TypingLanguage.English;
        var joint = _joint.Evaluate(word, _dictionary, true, true);
        var auto = _autocorrection.Evaluate(word, language, _dictionary);

        Assert.NotEqual(JointCorrectionRecommendation.Apply, joint.Recommendation);
        Assert.Equal(AutocorrectionRecommendation.NoChange, auto.Recommendation);
    }

    [Theory]
    [InlineData("миняй", "меняй")]
    [InlineData("vbyzq", "меняй")]
    [InlineData("напесал", "написал")]
    [InlineData("дууш", "душ")]
    [InlineData("gtie", "пишу")]
    [InlineData("ghbdtn", "привет")]
    public async Task LivePipeline_DoubleShiftUndo_RestoresOriginal(string typo, string corrected)
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

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal(corrected, replacement.LastReplacement);

        var undo = await undoService.TryUndoAsync();
        Assert.Equal(CorrectionUndoOutcome.Success, undo.Outcome);
        Assert.Equal($"{corrected} ", replacement.LastOriginal);
        Assert.Equal($"{typo} ", replacement.LastReplacement);
    }

    [Fact]
    public async Task LivePipeline_ExactWord_DeliversSpaceOnceWithoutReplacement()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement,
            delivery);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "дать");
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(0, replacement.CallCount);
        Assert.Equal(1, delivery.DeliverCallCount);
    }

    [Fact]
    public async Task LivePipeline_CombinedReplacement_HappensOnce()
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement,
            delivery);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, "vbyzq");
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(1, replacement.CallCount);
        Assert.Equal("меняй", replacement.LastReplacement);
        Assert.Equal(1, delivery.DeliverCallCount);
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
