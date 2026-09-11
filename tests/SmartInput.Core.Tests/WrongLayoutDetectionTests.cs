using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

namespace SmartInput.Core.Tests;

public class WrongLayoutDetectionTests
{
    private readonly WrongLayoutDetectionService _service = new(new KeyboardLayoutConverter());

    private static readonly ActiveLanguageSet BothLanguages = ActiveLanguageSet.EnglishAndRussian;

    [Fact]
    public void Evaluate_Ghbdtn_SuggestsPrivet()
    {
        var result = _service.Evaluate("ghbdtn", BothLanguages);

        Assert.Equal("ghbdtn", result.OriginalToken);
        Assert.Equal("привет", result.CandidateToken);
        Assert.Equal(LayoutConversionDirection.EnglishToRussian, result.ConversionDirection);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_Ruddsh_SuggestsHello()
    {
        var result = _service.Evaluate("руддщ", BothLanguages);

        Assert.Equal("руддщ", result.OriginalToken);
        Assert.Equal("hello", result.CandidateToken);
        Assert.Equal(LayoutConversionDirection.RussianToEnglish, result.ConversionDirection);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_KnownRussianCandidate_OverridesAmbiguousLatinPlausibility()
    {
        var service = new WrongLayoutDetectionService(
            new KeyboardLayoutConverter(),
            CreateStarterDictionary());

        var result = service.Evaluate("vbh", BothLanguages);

        Assert.Equal("мир", result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_BundledRussianFrequencyWord_SuggestsText()
    {
        var service = new WrongLayoutDetectionService(
            new KeyboardLayoutConverter(),
            CreateBundledDictionary());

        var result = service.Evaluate("ntrcn", BothLanguages);

        Assert.Equal("текст", result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
    }

    [Theory]
    [InlineData("акщтеутв", "frontend")]
    [InlineData("ифслутв", "backend")]
    [InlineData("афые", "fast")]
    [InlineData("фзш", "api")]
    [InlineData("djj,ot", "вообще")]
    public void Evaluate_GeneralPhysicalLayoutForms_UsesMappedTargetEvidence(
        string token,
        string expectedReplacement)
    {
        var service = new WrongLayoutDetectionService(
            new KeyboardLayoutConverter(),
            LiveCorrectionTestHelpers.CreateStarterDictionary());

        var result = service.Evaluate(token, BothLanguages);

        Assert.Equal(expectedReplacement, result.CandidateToken);
        Assert.Equal(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore >= WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Theory]
    [InlineData("z", "я", TypingLanguage.English)]
    [InlineData("ш", "i", TypingLanguage.Russian)]
    [InlineData("ру", "he", TypingLanguage.Russian)]
    [InlineData("vs", "мы", TypingLanguage.English)]
    [InlineData("'nj", "это", TypingLanguage.English)]
    [InlineData("rfr", "как", TypingLanguage.English)]
    [InlineData("jyb", "они", TypingLanguage.English)]
    [InlineData("t;br", "ежик", TypingLanguage.English)]
    [InlineData("[jnm", "хоть", TypingLanguage.English)]
    [InlineData("црщ", "who", TypingLanguage.Russian)]
    [InlineData("дуфл", "leak", TypingLanguage.Russian)]
    public void ShortExactLayoutPolicy_ConsidersEveryLengthFromOneToFour(
        string token,
        string expectedReplacement,
        TypingLanguage sourceLanguage)
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();

        Assert.True(ShortLayoutCandidatePolicy.IsInScope(token));
        Assert.True(BoundedCandidateApplyGuard.EvaluateDirectLayout(
            token,
            expectedReplacement,
            sourceLanguage,
            dictionary,
            new KeyboardLayoutConverter()) == BoundedApplyVerdict.Allow);
    }

    [Theory]
    [InlineData("a", "ф", TypingLanguage.English)]
    [InlineData("it", "ше", TypingLanguage.English)]
    [InlineData("he", "ру", TypingLanguage.English)]
    [InlineData("он", "jy", TypingLanguage.Russian)]
    [InlineData("мы", "vs", TypingLanguage.Russian)]
    public void ShortExactKnownSource_IsPreserved(
        string token,
        string mapped,
        TypingLanguage sourceLanguage)
    {
        var verdict = BoundedCandidateApplyGuard.EvaluateDirectLayout(
            token,
            mapped,
            sourceLanguage,
            LiveCorrectionTestHelpers.CreateStarterDictionary(),
            new KeyboardLayoutConverter());

        Assert.NotEqual(BoundedApplyVerdict.Allow, verdict);
    }

    [Fact]
    public void StandalonePunctuation_IsNotAWordLayoutToken()
    {
        Assert.Equal(TokenScript.Other, TokenScriptAnalyzer.ClassifyForLayout("."));
        Assert.Equal(TokenScript.Other, TokenScriptAnalyzer.ClassifyForLayout("'"));
        Assert.Equal(TokenScript.Latin, TokenScriptAnalyzer.ClassifyForLayout("'nj"));
    }

    [Fact]
    public void AllCapsShortAbbreviation_IsNotAServiceLayoutWord()
    {
        var result = new WrongLayoutDetectionService(
                new KeyboardLayoutConverter(),
                LiveCorrectionTestHelpers.CreateStarterDictionary())
            .Evaluate("VS", BothLanguages);

        Assert.NotEqual(LayoutDetectionRecommendation.Candidate, result.Recommendation);
    }

    private static TestAutocorrectDictionary CreateStarterDictionary()
    {
        var dictionary = new TestAutocorrectDictionary();
        dictionary.Add(TypingLanguage.English, "hello", 1.0);
        dictionary.Add(TypingLanguage.Russian, "мир", 0.90);
        return dictionary;
    }

    private static CompositeAutocorrectDictionary CreateBundledDictionary()
    {
        return new CompositeAutocorrectDictionary(
            new LiveCorrectionTestHelpers.FakeUserAutocorrectDictionaryStore([]));
    }

    [Fact]
    public void Evaluate_CorrectEnglishWord_ReturnsNoChange()
    {
        var result = _service.Evaluate("hello", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.True(result.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_CorrectRussianWord_ReturnsNoChange()
    {
        var result = _service.Evaluate("привет", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.True(result.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_ShortAmbiguousToken_ReturnsWaitOrNoChange()
    {
        var result = _service.Evaluate("gh", BothLanguages);

        Assert.NotEqual(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_MixedLanguageToken_ReturnsNoChange()
    {
        var result = _service.Evaluate("helloмир", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
        Assert.Null(result.CandidateToken);
    }

    [Theory]
    [InlineData("Cursor")]
    [InlineData("GitHub")]
    [InlineData("VSCode")]
    public void Evaluate_TechnicalNames_ReturnsNoChange(string token)
    {
        var result = _service.Evaluate(token, BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_Url_ReturnsNoChange()
    {
        var result = _service.Evaluate("https://example.com", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_EmailLikeToken_ReturnsNoChange()
    {
        var result = _service.Evaluate("user@example.com", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_FilePath_ReturnsNoChange()
    {
        var result = _service.Evaluate(@"C:\Projects\file.txt", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_Identifier_ReturnsNoChange()
    {
        var result = _service.Evaluate("myVariableName", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_SymbolToken_ReturnsNoChange()
    {
        var result = _service.Evaluate("$#@", BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_LowConfidenceCase_ReturnsNoChangeOrWait()
    {
        var result = _service.Evaluate("abc", BothLanguages);

        Assert.NotEqual(LayoutDetectionRecommendation.Candidate, result.Recommendation);
        Assert.True(result.ConfidenceScore < WrongLayoutDetectionOptions.DefaultCandidateThreshold);
    }

    [Fact]
    public void Evaluate_EmptyToken_ReturnsNoChange()
    {
        var result = _service.Evaluate(string.Empty, BothLanguages);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Equal(0.0, result.ConfidenceScore);
    }

    [Fact]
    public void Evaluate_EnglishOnly_DoesNotSuggestRussianCandidateForCyrillicToken()
    {
        var result = _service.Evaluate("руддщ", ActiveLanguageSet.EnglishOnly);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.ConversionDirection);
    }

    [Fact]
    public void Evaluate_RussianOnly_DoesNotSuggestEnglishCandidateForLatinToken()
    {
        var result = _service.Evaluate("ghbdtn", ActiveLanguageSet.RussianOnly);

        Assert.Equal(LayoutDetectionRecommendation.NoChange, result.Recommendation);
        Assert.Null(result.ConversionDirection);
    }

    [Fact]
    public void Evaluate_CustomThreshold_RespectsCandidateCutoff()
    {
        var strictOptions = new WrongLayoutDetectionOptions
        {
            CandidateThreshold = 0.99,
            WaitThreshold = 0.90,
        };

        var result = _service.Evaluate("ghbdtn", BothLanguages, strictOptions);

        Assert.Equal("привет", result.CandidateToken);
        Assert.True(result.ConfidenceScore < strictOptions.CandidateThreshold);
        Assert.Equal(LayoutDetectionRecommendation.Wait, result.Recommendation);
    }

    [Fact]
    public void Evaluate_DeterministicResults_ForSameInput()
    {
        var first = _service.Evaluate("ghbdtn", BothLanguages);
        var second = _service.Evaluate("ghbdtn", BothLanguages);

        Assert.Equal(first.Recommendation, second.Recommendation);
        Assert.Equal(first.ConfidenceScore, second.ConfidenceScore);
        Assert.Equal(first.CandidateToken, second.CandidateToken);
    }
}
