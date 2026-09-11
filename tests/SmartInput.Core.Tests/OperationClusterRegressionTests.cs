using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

/// <summary>
/// Bounded controlled corpus covering every operation cluster. Test-only artifact.
/// </summary>
[Trait("Category", "OperationClusterRegression")]
public partial class OperationClusterRegressionTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly JointCorrectionDecisionService _joint;

    public OperationClusterRegressionTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(CandidateAmbiguityIndex.ForStarterLexicon(converter)),
            converter);
    }

    public static IEnumerable<object[]> MustCorrect =>
    [
        ["миняй", "меняй"],
        ["vbyzq", "меняй"],
        ["деелай", "делай"],
        ["машиина", "машина"],
        ["говноо", "говно"],
        ["меняяй", "меняй"],
        ["дериись", "дерись"],
        ["пениис", "пенис"],
        ["будуут", "будут"],
        ["напесал", "написал"],
        ["исслидование", "исследование"],
        ["посянить", "пояснить"],
        ["спецеально", "специально"],
        ["испровляет", "исправляет"],
        ["обстаят", "обстоят"],
        ["допалнение", "дополнение"],
        ["дууш", "душ"],
        ["превет", "привет"],
        ["helo", "hello"],
        ["teh", "the"],
        ["adn", "and"],
        ["recieve", "receive"],
        ["becuase", "because"],
        ["thier", "their"],
        ["gtie", "пишу"],
        ["gbie", "пишу"],
        ["vtyzq", "меняй"],
        ["vtyz", "меня"],
        ["ghbdtn", "привет"],
        ["руддщ", "hello"],
        ["мущ", "veo"],
        ["пзг", "gpu"],
    ];

    public static IEnumerable<object[]> MustPreserve =>
    [
        ["начала"], ["начало"], ["дает"], ["даёт"], ["даст"], ["дают"], ["дать"],
        ["жать"], ["дал"], ["дела"], ["дело"], ["видел"], ["видик"], ["нас"], ["нам"],
        ["меня"], ["сеня"], ["неизвестное"], ["неизвестно"], ["гавно"], ["говно"],
        ["душ"], ["пишу"], ["все"], ["всё"], ["еще"], ["ещё"], ["идет"], ["идёт"], ["ждет"], ["ждёт"], ["начали"],
        ["касса"], ["ванна"], ["группа"], ["класс"], ["суббота"], ["Россия"],
        ["Алла"], ["Анна"], ["тонна"], ["сумма"], ["комиссия"], ["профессия"],
        ["территория"], ["искусство"], ["рассказ"], ["программа"],
        ["hello"], ["letter"], ["coffee"], ["class"], ["address"], ["success"], ["necessary"],
        ["parallel"], ["application"], ["correct"],
    ];

    public static IEnumerable<object[]> MustNotLayoutLeak =>
    [
        ["миняй", "vbyzq"],
        ["гавно", "ufdyj"],
        ["говно", "ujdyj"],
        ["дууш", "leei"],
        ["душ", "lei"],
        ["меня", "vtyz"],
        ["vbyzq", "vbyzq"],
        ["ufdyj", "ufdyj"],
        ["leei", "leei"],
    ];

    public static IEnumerable<object[]> MustNotApplyUnknown =>
    [
        ["ufdyj"],
        ["leei"],
        ["plhfdcndeqnt"],
    ];

    public static IEnumerable<object[]> MustApplyCapitalized =>
    [
        ["Миняй", "Меняй"],
        ["Превет", "Привет"],
        ["МИНЯЙ", "МЕНЯЙ"],
    ];

    [Fact]
    public void ControlledCorpus_NenLayoutAnchor_WithRussianContext()
    {
        var hint = new SentenceLanguageHint(TypingLanguage.Russian, 4, 0, 4, 20);
        var result = _joint.Evaluate("nen", _dictionary, true, true, languageHint: hint);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal("тут", result.ReplacementToken);
    }

    [Theory]
    [MemberData(nameof(MustCorrect))]
    public void ControlledCorpus_MustCorrect(string typo, string expected)
    {
        var result = _joint.Evaluate(typo, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(expected, result.ReplacementToken);
    }

    [Theory]
    [MemberData(nameof(MustPreserve))]
    public void ControlledCorpus_MustPreserve(string word)
    {
        var result = _joint.Evaluate(word, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Theory]
    [MemberData(nameof(MustNotApplyUnknown))]
    public void ControlledCorpus_MustNotApplyUnknown(string source)
    {
        var result = _joint.Evaluate(source, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Theory]
    [MemberData(nameof(MustApplyCapitalized))]
    public void ControlledCorpus_MustApplyCapitalized(string typo, string expected)
    {
        var result = _joint.Evaluate(typo, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(expected, result.ReplacementToken);
    }

    [Theory]
    [MemberData(nameof(MustNotLayoutLeak))]
    public void ControlledCorpus_MustNotLayoutLeak(string source, string forbidden)
    {
        var result = _joint.Evaluate(source, _dictionary, true, true);
        if (result.Recommendation == JointCorrectionRecommendation.Apply)
        {
            Assert.False(
                string.Equals(result.ReplacementToken, forbidden, StringComparison.OrdinalIgnoreCase),
                $"{source} leaked to {result.ReplacementToken}");
        }
    }
}
