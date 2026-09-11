using System.Security.Cryptography;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

if (args.Length >= 2 && string.Equals(args[0], "--stress-corpus", StringComparison.OrdinalIgnoreCase))
{
    var requestedRepetitions = args.Length >= 3
        && int.TryParse(args[2], out var parsedRepetitions)
        ? parsedRepetitions
        : 1;
    RunStressCorpus(args[1], Math.Clamp(requestedRepetitions, 1, 100));
    return;
}

if (args.Length >= 2 && string.Equals(args[0], "--layout", StringComparison.OrdinalIgnoreCase))
{
    RunLayoutProbe(args.Skip(1));
    return;
}

var words = new[]
{
    "првет", "стрвнно", "кмнда", "окороче", "мошина", "рвбота", "матемтка",
    "чешеш", "береш", "сделаеш", "сдлал", "распостраненный",
    "вада", "мыш", "нореально", "ппочему", "ннормально", "ппиздец",
    "достопрмечательность", "взаимопонмание", "предприимчвость", "непредсказумость",
    "самосовершенствовние", "удовлетварённость", "противоречвость", "последователность",
    "целеустрмлённость", "добросовесность", "непосредственость", "предрасположеность",
    "осведомлёность", "заинтересованость", "сосредоточеность", "благопрятный",
    "предварителный", "исключителный", "приблизителный", "самостоятелный",
    "обстоятелство", "доказателство", "свидетелство", "правопреемнк", "законодателство",
    "усовершенствовние", "функционировние", "взаимодействе", "сотрудничетво", "сопоставлние",
    "предположние", "предназначние", "предостережние", "предотвращние", "распрострнение",
    "происхождние", "преобразовние", "воспроизведние", "соприкосновние", "приспособлние",
    "разочаровние", "злоупотреблние", "противостояне", "мировоззрние", "самоопределние",
    "самоутверждние", "неудовлетварённость", "конфиденциалность", "интелектуальный",
    "професиональный", "териториальный", "эксперементальный", "диференциальный", "идентификаця",
    "классификаця", "систематизаця", "персонификаця", "интерпретаця", "конфигураця",
    "синхронизаця", "авторизаця", "аутентификаця", "реструктуризаця", "децентрализаця",
    "инфраструктра", "архитектра", "номенклатра", "температра", "литератра", "клавиатра",
    "магистратра", "прокуратра", "конкурентоспосбность", "платёжеспосбность", "работоспосбность",
    "жизнеспосбность", "стрессоустойчвость", "производителность", "продолжителность",
    "действителность", "чувствителность", "впечатлителность", "предусмотрителность",
    "исполнителность", "обязателность", "самодостаточнсть", "неприкосновеность", "неопределёность",
    "многофункционалность", "конкурентоспособнсть", "нецелесообразнсть", "непоследователность",
    "взаимозависмость", "взаимозаменямость", "непротиворечвость", "закономернсть", "принадлежнсть",
    "осуществлние", "предпринимателство", "делопроизводтво", "машиностроние", "градостроителство",
    "здравоохранние"
};

var dictionary = new CompositeAutocorrectDictionary(
    new EmptyUserDictionaryStore(),
    new HunspellWordFormProvider());
var provider = new CompositeExternalSpellCorrectionProvider(
    new SymSpellSpellCorrectionProvider(),
    new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));
var service = new AutocorrectionService(
    externalEvaluator: new ExternalAutocorrectionEvaluator(),
    externalProvider: provider);

var changed = 0;
var waited = 0;
var unchanged = 0;
var decisionLatenciesMs = new List<double>(words.Length);
foreach (var word in words)
{
    _ = service.Evaluate(
        word,
        TypingLanguage.Russian,
        dictionary,
        new SmartInput.Core.Configuration.AutocorrectionOptions { UseExternalProvider = true });
}
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

using var resultFingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
foreach (var word in words)
{
    var decisionWatch = System.Diagnostics.Stopwatch.StartNew();
    var result = service.Evaluate(
        word,
        TypingLanguage.Russian,
        dictionary,
        new SmartInput.Core.Configuration.AutocorrectionOptions { UseExternalProvider = true });
    decisionWatch.Stop();
    decisionLatenciesMs.Add(decisionWatch.Elapsed.TotalMilliseconds);

    switch (result.Recommendation)
    {
        case AutocorrectionRecommendation.Candidate:
            changed++;
            break;
        case AutocorrectionRecommendation.Wait:
            waited++;
            break;
        default:
            unchanged++;
            break;
    }

    var caseMaterial = $"{word}\u0000{result.Recommendation}\u0000{result.CandidateToken ?? string.Empty}";
    resultFingerprint.AppendData(System.Text.Encoding.UTF8.GetBytes(caseMaterial));
}

var fingerprint = Convert.ToHexString(resultFingerprint.GetHashAndReset()).ToLowerInvariant();
Console.WriteLine($"RESULT_FINGERPRINT\tsha256={fingerprint}");
Console.WriteLine($"SUMMARY\ttotal={words.Length}\tapply={changed}\twait={waited}\tnochange={unchanged}");
decisionLatenciesMs.Sort();
var p50Index = Math.Min(decisionLatenciesMs.Count - 1, (int)Math.Ceiling(decisionLatenciesMs.Count * 0.50) - 1);
var p95Index = Math.Min(decisionLatenciesMs.Count - 1, (int)Math.Ceiling(decisionLatenciesMs.Count * 0.95) - 1);
Console.WriteLine(
    $"PERF\tcases={decisionLatenciesMs.Count}"
    + $"\tp50_ms={decisionLatenciesMs[p50Index]:F3}"
    + $"\tp95_ms={decisionLatenciesMs[p95Index]:F3}"
    + $"\tmax_ms={decisionLatenciesMs[^1]:F3}"
    + $"\taverage_ms={decisionLatenciesMs.Average():F3}");

static void RunStressCorpus(string corpusPath, int repetitions)
{
    if (!File.Exists(corpusPath))
    {
        Console.Error.WriteLine("STRESS_ERROR\tcorpus_not_found");
        Environment.ExitCode = 2;
        return;
    }

    var dictionary = new CompositeAutocorrectDictionary(
        new EmptyUserDictionaryStore(),
        new HunspellWordFormProvider());
    var cases = StressCorpus.Load(corpusPath, dictionary);
    var provider = new CompositeExternalSpellCorrectionProvider(
        new SymSpellSpellCorrectionProvider(),
        new HunspellExternalSpellCorrectionProvider(new HunspellWordFormProvider()));
    var service = new AutocorrectionService(
        externalEvaluator: new ExternalAutocorrectionEvaluator(),
        externalProvider: provider);
    var options = new SmartInput.Core.Configuration.AutocorrectionOptions
    {
        UseExternalProvider = true,
    };

    var warmup = cases.Take(Math.Min(32, cases.Count)).ToArray();
    foreach (var item in warmup)
    {
        _ = service.Evaluate(item.Input, TypingLanguage.Russian, dictionary, options);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var before = Environment.WorkingSet;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var applyCorrect = 0;
    var applyWrong = 0;
    var expectedMiss = 0;
    var preserved = 0;
    var falsePositive = 0;
    var ambiguousApplied = 0;
    var operations = 0;
    var sectionStats = new Dictionary<int, StressSectionStats>();
    var fingerprint = System.Security.Cryptography.SHA256.Create();

    for (var repetition = 0; repetition < repetitions; repetition++)
    {
        foreach (var item in cases)
        {
            if (!sectionStats.TryGetValue(item.Section, out var stats))
            {
                stats = new StressSectionStats();
                sectionStats[item.Section] = stats;
            }

            var result = service.Evaluate(
                item.Input,
                TypingLanguage.Russian,
                dictionary,
                options);
            operations++;

            if (item.Kind == StressCaseKind.Correction)
            {
                if (result.Recommendation == AutocorrectionRecommendation.Candidate)
                {
                    if (EquivalentExpected(result.CandidateToken, item.Expected))
                    {
                        applyCorrect++;
                        stats.CorrectApply++;
                    }
                    else
                    {
                        applyWrong++;
                        stats.WrongApply++;
                    }
                }
                else
                {
                    expectedMiss++;
                    stats.ExpectedMiss++;
                }
            }
            else if (item.Kind == StressCaseKind.Preserve)
            {
                if (result.Recommendation == AutocorrectionRecommendation.Candidate
                    && !EquivalentExpected(result.CandidateToken, item.Input))
                {
                    falsePositive++;
                    stats.FalsePositive++;
                }
                else
                {
                    preserved++;
                    stats.Preserved++;
                }
            }
            else
            {
                if (result.Recommendation == AutocorrectionRecommendation.Candidate)
                {
                    ambiguousApplied++;
                    stats.AmbiguousApply++;
                }
                else
                {
                    stats.AmbiguousKept++;
                }
            }

            var caseId = $"{item.Kind}|{item.Section}|{item.Input}|{item.Expected}";
            var digest = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(caseId));
            fingerprint.TransformBlock(digest, 0, digest.Length, null, 0);
        }
    }

    stopwatch.Stop();
    var after = Environment.WorkingSet;
    var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
    var perOpUs = operations == 0 ? 0 : elapsedMs * 1000.0 / operations;
    var recoveryDenominator = applyCorrect + expectedMiss;
    var recoveryRate = recoveryDenominator == 0
        ? 1.0
        : (double)applyCorrect / recoveryDenominator;
    var fingerprintBytes = new byte[0];
    fingerprint.TransformFinalBlock([], 0, 0);
    fingerprintBytes = fingerprint.Hash ?? [];

    Console.WriteLine($"STRESS_CORPUS\tcases={cases.Count}\trepetitions={repetitions}\toperations={operations}");
    Console.WriteLine($"STRESS_CORRECTIONS\tcorrect_apply={applyCorrect}\texpected_miss={expectedMiss}\twrong_apply={applyWrong}\trecovery={recoveryRate:P2}");
    Console.WriteLine($"STRESS_PRESERVE\tpreserved={preserved}\tfalse_positive={falsePositive}");
    Console.WriteLine($"STRESS_AMBIGUOUS\tdeferred_or_kept={cases.Count(item => item.Kind == StressCaseKind.Ambiguous) * repetitions - ambiguousApplied}\tapplied={ambiguousApplied}");
    Console.WriteLine($"STRESS_PERF\telapsed_ms={elapsedMs:F1}\tper_operation_us={perOpUs:F1}\tworking_set_before_bytes={before}\tworking_set_after_bytes={after}\tworking_set_delta_bytes={after - before}");
    Console.WriteLine($"STRESS_FINGERPRINT\tsha256={Convert.ToHexString(fingerprintBytes).ToLowerInvariant()}");
    foreach (var pair in sectionStats.OrderBy(static pair => pair.Key))
    {
        var stats = pair.Value;
        Console.WriteLine(
            $"STRESS_SECTION\tsection={pair.Key}\tcorrect_apply={stats.CorrectApply}\texpected_miss={stats.ExpectedMiss}"
            + $"\twrong_apply={stats.WrongApply}\tpreserved={stats.Preserved}\tfalse_positive={stats.FalsePositive}"
            + $"\tambiguous_kept={stats.AmbiguousKept}\tambiguous_apply={stats.AmbiguousApply}");
    }
    Console.WriteLine("STRESS_NOTE\tOnly single-token pairs were executed; phrase, grammar, agreement, context and punctuation examples were excluded from this word-level run.");

    if (applyWrong > 0 || falsePositive > 0 || ambiguousApplied > 0)
    {
        Environment.ExitCode = 1;
    }
}


static void RunLayoutProbe(IEnumerable<string> inputWords)
{
    var wordForms = new HunspellWordFormProvider();
    var dictionary = new CompositeAutocorrectDictionary(
        new EmptyUserDictionaryStore(),
        wordForms);
    var converter = new KeyboardLayoutConverter();
    var detector = new WrongLayoutDetectionService(converter, dictionary);

    foreach (var token in inputWords.Where(static value => !string.IsNullOrWhiteSpace(value)))
    {
        var script = token.All(static character =>
                character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or ',')
            ? LayoutConversionDirection.EnglishToRussian
            : LayoutConversionDirection.RussianToEnglish;
        var mapped = converter.Convert(token, script);
        var result = detector.Evaluate(token, ActiveLanguageSet.EnglishAndRussian);
        var sourceLanguage = script == LayoutConversionDirection.RussianToEnglish
            ? TypingLanguage.Russian
            : TypingLanguage.English;
        var mutation = MutationClassificationOracle.Analyze(token, sourceLanguage, dictionary);
        Console.WriteLine(
            $"INPUT={token}\tMAPPED={mapped}\tRECOMMENDATION={result.Recommendation}"
            + $"\tCONFIDENCE={result.ConfidenceScore:F3}\tCANDIDATE={result.CandidateToken ?? "-"}"
            + $"\tMUTATION={mutation.Class}\tSOURCES={mutation.Sources.Count}"
            + $"\tTOP_SOURCE={mutation.UniqueTarget ?? "-"}");
    }

    wordForms.Dispose();
}

static bool EquivalentExpected(string? actual, string expected)
{
    if (actual is null)
    {
        return false;
    }

    if (string.Equals(actual, expected, StringComparison.Ordinal))
    {
        return true;
    }

    // Russian dictionaries commonly normalize ё to е. Treat this as the
    // same spelling for evaluation, while the runtime preserves the chosen
    // dictionary form in the actual replacement.
    return actual.Replace('ё', 'е').Replace('Ё', 'Е')
        .Equals(expected.Replace('ё', 'е').Replace('Ё', 'Е'), StringComparison.Ordinal);
}

sealed class EmptyUserDictionaryStore : IUserAutocorrectDictionaryStore
{
    public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries => [];
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

sealed class StressSectionStats
{
    public int CorrectApply { get; set; }
    public int ExpectedMiss { get; set; }
    public int WrongApply { get; set; }
    public int Preserved { get; set; }
    public int FalsePositive { get; set; }
    public int AmbiguousKept { get; set; }
    public int AmbiguousApply { get; set; }
}

enum StressCaseKind
{
    Correction,
    Preserve,
    Ambiguous,
}

sealed record StressCase(StressCaseKind Kind, int Section, string Input, string Expected);

static class StressCorpus
{
    private static readonly System.Text.RegularExpressions.Regex HeadingRegex =
        new(@"^#+\s*(\d+)\.", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex TokenRegex =
        new(@"^\p{L}+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static IReadOnlyList<StressCase> Load(string path, IAutocorrectDictionary dictionary)
    {
        var cases = new List<StressCase>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var section = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            var heading = HeadingRegex.Match(line);
            if (heading.Success)
            {
                section = int.Parse(heading.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            var arrow = line.IndexOf('→');
            if (arrow <= 0 || arrow >= line.Length - 1)
            {
                continue;
            }

            var input = line[..arrow].Trim();
            var expected = line[(arrow + 1)..].Trim();
            if (!TokenRegex.IsMatch(input) || !TokenRegex.IsMatch(expected))
            {
                continue;
            }

            var kind = input.Equals(expected, StringComparison.Ordinal)
                || section == 18
                ? StressCaseKind.Preserve
                : section == 17 && dictionary.Contains(input, TypingLanguage.Russian)
                    ? StressCaseKind.Ambiguous
                    : StressCaseKind.Correction;
            var key = $"{kind}|{section}|{input}|{expected}";
            if (seen.Add(key))
            {
                cases.Add(new StressCase(kind, section, input, expected));
            }
        }

        return cases;
    }
}
