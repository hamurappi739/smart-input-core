using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using SmartInput.Core.Diagnostics;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

/// <summary>
/// Verifies the same local provider chain that the resident host uses when
/// ExternalSpellingEngineEnabled is on. These are intentionally representative
/// one-edit Russian cases, not a per-word correction table.
/// </summary>
public sealed class LiveExternalSpellingRegressionTests
{
    [Theory]
    [InlineData("стрвнно", "странно")]
    [InlineData("кмнда", "команда")]
    [InlineData("здрвствуйте", "здравствуйте")]
    [InlineData("првет", "привет")]
    [InlineData("машына", "машина")]
    [InlineData("жызнь", "жизнь")]
    [InlineData("окороче", "короче")]
    [InlineData("мошина", "машина")]
    [InlineData("рвбота", "работа")]
    [InlineData("матемтка", "математика")]
    [InlineData("распостраненный", "распространенный")]
    [InlineData("чешеш", "чешешь")]
    [InlineData("береш", "берешь")]
    [InlineData("сделаеш", "сделаешь")]
    [InlineData("сдлал", "сделал")]
    [InlineData("жыть", "жить")]
    [InlineData("чюдо", "чудо")]
    [InlineData("мыш", "мышь")]
    [InlineData("вада", "вода")]
    [InlineData("вермя", "время")]
    [InlineData("человке", "человек")]
    [InlineData("интелектуалный", "интеллектуальный")]
    [InlineData("професионалный", "профессиональный")]
    [InlineData("исправлене", "исправление")]
    [InlineData("приложене", "приложение")]
    [InlineData("работть", "работать")]
    [InlineData("букво", "буква")]
    [InlineData("рабта", "работа")]
    [InlineData("сисема", "система")]
    [InlineData("прривет", "привет")]
    [InlineData("приввет", "привет")]
    [InlineData("приветт", "привет")]
    [InlineData("порограмма", "программа")]
    [InlineData("програмам", "программа")]
    [InlineData("малако", "молоко")]
    [InlineData("здравствуте", "здравствуйте")]
    [InlineData("слво", "слово")]
    [InlineData("слоов", "слово")]
    [InlineData("привед", "привет")]
    [InlineData("программма", "программа")]
    [InlineData("интерфес", "интерфейс")]
    [InlineData("интерфеейс", "интерфейс")]
    public void ResidentProviderChain_FindsRussianOneEditCorrection(string source, string expected)
    {
        var dictionary = new CompositeAutocorrectDictionary(
            new EmptyUserDictionaryStore(),
            new HunspellWordFormProvider());
        var provider = new CompositeExternalSpellCorrectionProvider(
            new SymSpellSpellCorrectionProvider(),
            new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));
        var evaluator = new ExternalAutocorrectionEvaluator();
        var candidates = provider.FindCandidates(source, TypingLanguage.Russian, 2);

        var service = new AutocorrectionService(
            externalEvaluator: evaluator,
            externalProvider: provider,
            settingsService: new LiveCorrectionTestHelpers.FakeSettingsService(
                new AppSettings { ExternalSpellingEngineEnabled = true }));
        var result = service.Evaluate(
            source,
            TypingLanguage.Russian,
            dictionary,
            new AutocorrectionOptions());
        Assert.True(
            result.Recommendation == AutocorrectionRecommendation.Candidate
                && string.Equals(result.CandidateToken, expected, StringComparison.OrdinalIgnoreCase),
            $"{source}: {result.Recommendation} -> {result.CandidateToken ?? "<none>"}; "
            + $"candidates={candidates.Count}");
    }

    [Theory]
    [InlineData("стрвнно", "странно")]
    [InlineData("кмнда", "команда")]
    [InlineData("здрвствуйте", "здравствуйте")]
    [InlineData("окороче", "короче")]
    [InlineData("мошина", "машина")]
    [InlineData("рвбота", "работа")]
    [InlineData("матемтка", "математика")]
    [InlineData("распостраненный", "распространенный")]
    [InlineData("чешеш", "чешешь")]
    [InlineData("береш", "берешь")]
    [InlineData("сделаеш", "сделаешь")]
    [InlineData("сдлал", "сделал")]
    public void ResidentProviderChain_JointDecisionAppliesRussianCorrection(string source, string expected)
    {
        var dictionary = new CompositeAutocorrectDictionary(
            new EmptyUserDictionaryStore(),
            new HunspellWordFormProvider());
        var provider = new CompositeExternalSpellCorrectionProvider(
            new SymSpellSpellCorrectionProvider(),
            new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));
        var settings = new LiveCorrectionTestHelpers.FakeSettingsService(
            new AppSettings { ExternalSpellingEngineEnabled = true });
        var autocorrection = new AutocorrectionService(
            externalEvaluator: new ExternalAutocorrectionEvaluator(),
            externalProvider: provider,
            settingsService: settings);
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            autocorrection,
            converter);

        var result = joint.Evaluate(
            source,
            dictionary,
            layoutEnabled: true,
            autocorrectEnabled: true);

        Assert.True(
            result.Recommendation == JointCorrectionRecommendation.Apply
                && string.Equals(result.ReplacementToken, expected, StringComparison.OrdinalIgnoreCase),
            $"{source}: {result.Recommendation} -> {result.ReplacementToken ?? "<none>"}/{result.Kind}");
    }

    [Theory]
    [InlineData("интелектуальный", "интеллектуальный")]
    [InlineData("професиональный", "профессиональный")]
    public void ResidentProviderChain_RecoversMissingDoubleConsonant(
        string source,
        string expected)
    {
        var dictionary = new CompositeAutocorrectDictionary(
            new EmptyUserDictionaryStore(),
            new HunspellWordFormProvider());
        var provider = new CompositeExternalSpellCorrectionProvider(
            new SymSpellSpellCorrectionProvider(),
            new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));
        var settings = new LiveCorrectionTestHelpers.FakeSettingsService(
            new AppSettings { ExternalSpellingEngineEnabled = true });
        var service = new AutocorrectionService(
            externalEvaluator: new ExternalAutocorrectionEvaluator(),
            externalProvider: provider,
            settingsService: settings);

        var result = service.Evaluate(
            source,
            TypingLanguage.Russian,
            dictionary,
            new AutocorrectionOptions());

        var deterministic = RussianOrthographyHeuristics.TryGetDeterministicCorrection(
            source,
            TypingLanguage.Russian,
            dictionary,
            out var deterministicTarget);

        Assert.True(
            result.Recommendation == AutocorrectionRecommendation.Candidate
                && string.Equals(result.CandidateToken, expected, StringComparison.OrdinalIgnoreCase),
            $"{source}: {result.Recommendation} -> {result.CandidateToken ?? "<none>"}; "
            + $"contains={dictionary.Contains(source, TypingLanguage.Russian)}; "
            + $"sourceFrequency={dictionary.GetFrequency(source, TypingLanguage.Russian):F3}; "
            + $"preserve={RussianOrthographyHeuristics.ShouldPreserveExactWord(source, TypingLanguage.Russian, dictionary)}; "
            + $"deterministic={deterministic}:{deterministicTarget}");
    }

    [Theory]
    [InlineData("стрвнно", "странно")]
    [InlineData("кмнда", "команда")]
    [InlineData("здрвствуйте", "здравствуйте")]
    public async Task ResidentProviderChain_EngineAppliesRussianCorrection(string source, string expected)
    {
        var dictionary = new CompositeAutocorrectDictionary(
            new EmptyUserDictionaryStore(),
            new HunspellWordFormProvider());
        var provider = new CompositeExternalSpellCorrectionProvider(
            new SymSpellSpellCorrectionProvider(),
            new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));
        var settings = new LiveCorrectionTestHelpers.FakeSettingsService(new AppSettings
        {
            IsEnabled = true,
            AutomaticLayoutEnabled = true,
            AutocorrectEnabled = true,
            ExternalSpellingEngineEnabled = true,
        });
        var autocorrection = new AutocorrectionService(
            externalEvaluator: new ExternalAutocorrectionEvaluator(),
            externalProvider: provider,
            settingsService: settings);
        var converter = new KeyboardLayoutConverter();
        var joint = new JointCorrectionDecisionService(
            new WrongLayoutDetectionService(converter, dictionary),
            autocorrection,
            converter);
        var replacement = new LiveCorrectionTestHelpers.FakeReplacementService(success: true);
        var engine = new AutomaticLayoutCorrectionEngine(
            new WrongLayoutDetectionService(converter, dictionary),
            autocorrection,
            dictionary,
            LiveCorrectionTestHelpers.CreateEmptySnippetService(),
            replacement,
            new LiveCorrectionTestHelpers.FakeAutomationSafetyService(LiveCorrectionTestHelpers.AllowedPolicy()),
            settings,
            new LiveCorrectionTestHelpers.FakeBoundaryDeliveryService(),
            new LiveCorrectionTestHelpers.FakeCorrectionUndoService(),
            new CorrectionApplicationContext(),
            NullPerformanceMetricsRecorder.Instance,
            jointCorrectionDecisionService: joint);

        await LiveCorrectionTestHelpers.TypeTokenAsync(engine, source);
        await engine.ProcessInputAsync(
            TokenInputEvent.Boundary(LiveCorrectionTestHelpers.SpaceBoundaryKey()));

        Assert.Equal(expected, replacement.LastReplacement);
    }

    private sealed class EmptyUserDictionaryStore : IUserAutocorrectDictionaryStore
    {
        public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries => [];

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
