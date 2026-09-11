using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Persistence;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Security;
using SmartInput.Platform.Abstractions.Text;

namespace SmartInput.Core.Tests;

[Trait("Category", "FullCorpusAudit")]
public class MaximumCorpusAuditTests
{
    // Product acceptance currently prioritizes zero false applies over recall.
    // This floor is aligned with the agreed 89.74% minimum while keeping a
    // safety margin for held-out corpus variance.
    private const double RecoveryAcceptanceFloor = 0.90;
    private const int ExpectedEnglishExactServiceWords = 13;

    [Theory]
    [InlineData(42)]
    [InlineData(1337)]
    [InlineData(20260903)]
    public void FullCorpusMutationAudit_MeetsAcceptanceCriteria(int seed)
    {
        var report = MaximumCorpusAuditHarness.RunFullAudit(
            seed,
            minRussianMutations: 125_000,
            minEnglishMutations: 125_000);

        Assert.True(report.RussianExactWords >= 40_000, report.FormatSummary());
        Assert.True(report.EnglishExactWords >= 40_000, report.FormatSummary());
        Assert.Equal(ExpectedEnglishExactServiceWords, report.EnglishExactServiceWords);
        Assert.Equal(
            report.EnglishExactServiceWords,
            report.ExactServiceWordsCorrectlyConverted
                + report.ExactServiceWordsWaited
                + report.ExactServiceWordsWrong);
        Assert.Equal(
            report.RussianExactWords + report.EnglishExactWords - report.EnglishExactServiceWords,
            report.ExactWordsPreserved + report.ExactWordsChanged - report.ExactServiceWordsWrong);
        Assert.True(
            report.SumTerminalCategories() == report.TotalTokensEvaluated,
            $"terminals={report.SumTerminalCategories()} tokens={report.TotalTokensEvaluated}; {report.FormatSummary()}");
        Assert.True(report.RussianMutationsEvaluated >= 125_000, report.FormatSummary());
        Assert.True(report.EnglishMutationsEvaluated >= 125_000, report.FormatSummary());
        Assert.True(
            report.RussianMutationsEvaluated + report.EnglishMutationsEvaluated >= 250_000,
            report.FormatSummary());
        Assert.True(
            report.ExactWordsChanged == 0,
            $"ExactWordsChanged={report.ExactWordsChanged}; samples={string.Join(" | ", report.FailureSamples.Take(15))}; {report.FormatSummary()}");
        Assert.True(
            report.DirectLayoutAccepted == 0,
            $"DirectLayoutAccepted={report.DirectLayoutAccepted}; samples={string.Join(" | ", report.FailureSamples.Take(15))}; {report.FormatSummary()}");
        Assert.True(
            report.KnownToKnownFalseSubstitutions == 0,
            report.FormatSummary());
        Assert.True(
            report.TotalAmbiguousApplied == 0,
            $"TotalAmbiguousApplied={report.TotalAmbiguousApplied}; "
            + $"(spelling={report.AmbiguousSpellingApplied},layout={report.AmbiguousDirectLayoutApplied},"
            + $"combined={report.AmbiguousCombinedApplied}); "
            + $"samples={string.Join(" | ", report.FailureSamples.Take(20))}; {report.FormatSummary()}");
        Assert.True(
            report.WrongUniqueTarget == 0,
            $"WrongUniqueTarget={report.WrongUniqueTarget}; {report.FormatSummary()}");
        Assert.True(
            report.WrongDirectLayout == 0,
            $"WrongDirectLayout={report.WrongDirectLayout}; {report.FormatSummary()}");
        Assert.True(
            report.WrongCombined == 0,
            $"WrongCombined={report.WrongCombined}; {report.FormatSummary()}");
        Assert.Equal(
            report.OracleUniquelyRecoverable,
            report.CorrectMutationRecoveries + report.MissedRecoveries + report.WrongUniqueTarget);
        Assert.Equal(
            report.OracleUniquelyRecoverable,
            CorpusAuditAccounting.SumUniqueTerminals(report));
        Assert.Equal(
            report.OracleAmbiguous,
            CorpusAuditAccounting.SumAmbiguousTerminals(report));
        Assert.True(
            report.WrongConfidentCorrections == 0,
            $"WrongConfident={report.WrongConfidentCorrections}; clusters={FormatClusters(report)}; samples={string.Join(" | ", report.FailureSamples.Take(20))}; {report.FormatSummary()}");
        Assert.True(
            report.WrongLayoutTargets == 0,
            $"WrongLayoutTargets={report.WrongLayoutTargets}; samples={string.Join(" | ", report.FailureSamples.Take(15))}; {report.FormatSummary()}");
        Assert.True(
            report.WrongCombinedTargets == 0,
            report.FormatSummary());
        Assert.True(
            report.LayoutShouldConvertCorrect > 0,
            $"positive layout coverage missing; {report.FormatSummary()}");
        Assert.True(
            report.EligibleUnambiguousMutations > 0,
            report.FormatSummary());
        Assert.True(
            report.RecoveryRate >= RecoveryAcceptanceFloor,
            $"RecoveryRate={report.RecoveryRate:P2}; floor={RecoveryAcceptanceFloor:P0}; eligible={report.EligibleUnambiguousMutations}; "
            + $"recoveries={report.CorrectMutationRecoveries}; missed={report.MissedRecoveries}; {report.FormatSummary()}");
        Assert.True(report.AverageMs < 5.0, report.FormatSummary());
        Assert.True(report.P95Ms < 25.0, report.FormatSummary());
        Assert.True(report.MaxMs < 150.0, report.FormatSummary());
        Assert.True(report.MaxGeneratedCandidates <= 512, report.FormatSummary());
        Assert.True(report.TotalTokensEvaluated > 250_000, report.FormatSummary());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(2024)]
    [InlineData(8675309)]
    [InlineData(20260904)]
    [InlineData(31415926)]
    [Trait("Category", "HeldOutCorpusAudit")]
    public void HeldOutCorpusMutationAudit_MeetsAcceptanceCriteria(int seed)
    {
        FullCorpusMutationAudit_MeetsAcceptanceCriteria(seed);
    }

    private static string FormatClusters(MaximumCorpusAuditReport report)
    {
        return string.Join(
            ",",
            report.WrongConfidentClusters.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    [Fact]
    public void RepeatedCharacterMutations_HighFrequencyWords_HaveZeroWrongConfident()
    {
        var dictionary = LiveCorrectionTestHelpers.CreateStarterDictionary();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(new KeyboardLayoutConverter(), dictionary),
            new AutocorrectionService(),
            new KeyboardLayoutConverter());

        var words = StarterAutocorrectLexicon.Entries
            .Where(pair => pair.Key.Language == TypingLanguage.Russian && pair.Value >= 0.90)
            .Select(pair => pair.Key.Word)
            .Where(word => word.Length is >= 4 and <= 10)
            .Take(3_000)
            .ToList();

        var random = new Random(42);
        var wrong = 0;
        var corrected = 0;
        foreach (var word in words)
        {
            var index = random.Next(word.Length);
            if (index + 1 < word.Length && word[index] == word[index + 1])
            {
                continue;
            }

            var typo = word.Insert(index, word[index].ToString());
            if (TrustedWordAnalyzer.IsExactKnownOriginal(typo, dictionary))
            {
                continue;
            }

            var result = joint.Evaluate(typo, dictionary, true, true);
            if (result.Recommendation != JointCorrectionRecommendation.Apply
                || string.IsNullOrEmpty(result.ReplacementToken))
            {
                continue;
            }

            if (string.Equals(result.ReplacementToken, word, StringComparison.Ordinal))
            {
                corrected++;
            }
            else if (TrustedWordAnalyzer.IsExactKnownOriginal(result.ReplacementToken, dictionary))
            {
                wrong++;
            }
        }

        Assert.True(corrected > 100);
        Assert.Equal(0, wrong);
    }
}

[Trait("Category", "FastRegression")]
public class MaximumMorphologyAndYeYoTests
{
    private readonly CompositeAutocorrectDictionary _dictionary =
        LiveCorrectionTestHelpers.CreateStarterDictionary();

    private readonly JointCorrectionDecisionService _joint;

    public MaximumMorphologyAndYeYoTests()
    {
        var converter = new KeyboardLayoutConverter();
        _joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, _dictionary),
            new AutocorrectionService(),
            converter);
    }

    [Theory]
    [InlineData("начало")]
    [InlineData("начала")]
    [InlineData("началу")]
    [InlineData("началом")]
    [InlineData("начале")]
    [InlineData("начал")]
    [InlineData("начали")]
    [InlineData("начать")]
    [InlineData("начинает")]
    [InlineData("началась")]
    [InlineData("дает")]
    [InlineData("даёт")]
    [InlineData("дают")]
    [InlineData("даст")]
    [InlineData("дать")]
    [InlineData("стала")]
    [InlineData("стало")]
    [InlineData("была")]
    [InlineData("было")]
    [InlineData("слова")]
    [InlineData("слово")]
    [InlineData("места")]
    [InlineData("место")]
    [InlineData("все")]
    [InlineData("всё")]
    [InlineData("еще")]
    [InlineData("ещё")]
    [InlineData("идет")]
    [InlineData("идёт")]
    [InlineData("ждет")]
    [InlineData("ждёт")]
    public void MorphologyAndYeYoForms_ArePreserved(string word)
    {
        var result = _joint.Evaluate(word, _dictionary, true, true);
        Assert.NotEqual(JointCorrectionRecommendation.Apply, result.Recommendation);
    }

    [Theory]
    [InlineData("миняй", "меняй")]
    [InlineData("Миняй", "Меняй")]
    [InlineData("МИНЯЙ", "МЕНЯЙ")]
    [InlineData("превет", "привет")]
    [InlineData("Превет", "Привет")]
    [InlineData("ПРЕВЕТ", "ПРИВЕТ")]
    public void Capitalization_IsPreservedOnCorrection(string typo, string expected)
    {
        var result = _joint.Evaluate(typo, _dictionary, true, true);
        Assert.Equal(JointCorrectionRecommendation.Apply, result.Recommendation);
        Assert.Equal(expected, result.ReplacementToken);
    }
}

[Trait("Category", "ApplicationPolicy")]
public class MaximumApplicationPolicyMatrixTests
{
    private readonly SafetyPolicyEvaluator _evaluator = new();

    public static IEnumerable<object[]> EditingApps() =>
    [
        ["notepad"], ["winword"], ["excel"], ["powerpnt"], ["outlook"], ["olk"],
        ["soffice"], ["wordpad"], ["chrome"], ["msedge"], ["firefox"], ["opera"],
        ["telegram"], ["Discord"], ["WhatsApp"], ["slack"], ["ms-teams"], ["Teams"],
        ["thunderbird"], ["obsidian"], ["notion"], ["zoom"],
    ];

    public static IEnumerable<object[]> SafeModeApps() =>
        DefaultSafeModeRules.ProcessNames.Select(static processName => new object[] { processName });

    [Theory]
    [MemberData(nameof(EditingApps))]
    public void EditingApplications_AllowAutomationWhenHealthy(string processName)
    {
        var result = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(processName: processName));
        Assert.Equal(AutomationPolicyState.Allowed, result.State);
        Assert.True(result.AllowsAutomation);
    }

    [Theory]
    [MemberData(nameof(SafeModeApps))]
    public void SafeModeApplications_BlockAutomation(string processName)
    {
        var result = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(processName: processName));
        Assert.Equal(AutomationPolicyState.SafeMode, result.State);
        Assert.False(result.AllowsAutomation);
    }

    [Theory]
    [InlineData("UnityWndClass")]
    [InlineData("UnrealWindow")]
    [InlineData("SDL_app")]
    [InlineData("Valve001")]
    public void GameWindowClasses_AreSafeMode(string windowClass)
    {
        var context = SafetyPolicyDefaults.CreateDefaultContext(
            processName: "game",
            windowClassName: windowClass);
        var result = _evaluator.Evaluate(context);
        Assert.False(result.AllowsAutomation);
    }

    [Theory]
    [InlineData(SecureInputState.Active)]
    [InlineData(SecureInputState.Unknown)]
    public void SecureInput_BlocksAllApps(SecureInputState state)
    {
        var result = _evaluator.Evaluate(SafetyPolicyDefaults.CreateDefaultContext(
            processName: "notepad",
            secureInputState: state));
        Assert.False(result.AllowsAutomation);
    }
}

[Trait("Category", "Privacy")]
public class MaximumPrivacyAuditTests
{
    [Fact]
    public void LiveLayoutCorrectionStatus_HasNoTokenTextFields()
    {
        var type = typeof(LiveLayoutCorrectionStatus);
        var stringProperties = type.GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(stringProperties, name =>
            name.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Word", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Candidate", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Replacement", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Context", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CorrectionNotificationMessages_DoNotEmbedSampleTokens()
    {
        Assert.DoesNotContain("миняй", CorrectionNotificationMessages.Autocorrect);
        Assert.DoesNotContain("меняй", CorrectionNotificationMessages.Autocorrect);
        Assert.DoesNotContain("привет", CorrectionNotificationMessages.Layout);
        Assert.DoesNotContain("hello", CorrectionNotificationMessages.Layout);
    }

    [Fact]
    public void PunctuationCorrectionResult_HasNoPersistedTextDiagnostics()
    {
        var type = typeof(PunctuationCorrectionResult);
        var stringProperties = type.GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .ToList();

        Assert.Contains("OriginalSegment", stringProperties);
        Assert.Contains("ReplacementSegment", stringProperties);
        Assert.DoesNotContain(stringProperties, name =>
            name.Contains("Context", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sentence", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CorrectionNotificationMessages_Punctuation_DoNotEmbedSampleTokens()
    {
        Assert.DoesNotContain("Привет", CorrectionNotificationMessages.Punctuation);
        Assert.DoesNotContain("мир", CorrectionNotificationMessages.Punctuation);
    }
}

[Trait("Category", "LivePipeline")]
public class MaximumLivePipelineRegressionTests
{
    private static TokenInputEvent SpaceBoundary() =>
        TokenInputEvent.Boundary(DeferredBoundaryKey.FromVirtualKey(VirtualKeys.Space, 57));

    [Theory]
    [InlineData("миняй", "меняй")]
    [InlineData("начала", null)]
    [InlineData("дает", null)]
    [InlineData("дать", null)]
    [InlineData("vbyzq", "меняй")]
    [InlineData("gtie", "пишу")]
    public async Task LivePipeline_SpaceBoundary_BehavesAsExpected(string token, string? expected)
    {
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var delivery = new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService();
        var engine = LiveCorrectionTestHelpers.CreateEngine(
            LiveCorrectionTestHelpers.AllowedPolicy(),
            LiveCorrectionTestHelpers.EnabledBothCorrectionSettings(),
            replacement,
            delivery);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, token);
        await engine.ProcessInputAsync(SpaceBoundary());

        Assert.Equal(1, delivery.DeliverCallCount);
        if (expected is null)
        {
            Assert.Equal(0, replacement.CallCount);
        }
        else
        {
            Assert.Equal(1, replacement.CallCount);
            Assert.Equal(expected, replacement.LastReplacement);
        }
    }

    [Theory]
    [InlineData("миняй", "меняй")]
    [InlineData("vbyzq", "меняй")]
    [InlineData("деелай", "делай")]
    [InlineData("будуут", "будут")]
    [InlineData("напесал", "написал")]
    [InlineData("дууш", "душ")]
    [InlineData("gtie", "пишу")]
    [InlineData("ghbdtn", "привет")]
    [InlineData("мущ", "veo")]
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
        Assert.Equal(corrected, replacement.LastReplacement);

        var undo = await undoService.TryUndoAsync();
        Assert.Equal(CorrectionUndoOutcome.Success, undo.Outcome);
        Assert.Equal($"{corrected} ", replacement.LastOriginal);
        Assert.Equal($"{typo} ", replacement.LastReplacement);
    }

    private sealed class TrackingReplacementService : ISafeTextReplacementService
    {
        public string? LastOriginal { get; private set; }
        public string? LastReplacement { get; private set; }

        public Task<TextReplacementResult> ReplaceRecentTextAsync(
            string originalText,
            string replacementText,
            CancellationToken cancellationToken = default)
        {
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
