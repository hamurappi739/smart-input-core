using System.Security.Cryptography;
using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Integration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Rust;

// Audit-only runner for the optional scorer. It deliberately prints no source
// or replacement text; only aggregate outcomes and candidate digests are used.
var dictionaryRoot = Environment.GetEnvironmentVariable("SMARTINPUT_HUNSPELL_ROOT");
var wordForms = string.IsNullOrWhiteSpace(dictionaryRoot)
    ? new HunspellWordFormProvider()
    : new HunspellWordFormProvider(HunspellDictionaryCatalog.Discover(dictionaryRoot));
var dictionary = new CompositeAutocorrectDictionary(new EmptyUserDictionaryStore(), wordForms);
var converter = new KeyboardLayoutConverter();
var decision = new JointCorrectionDecisionService(
    new WrongLayoutDetectionService(converter, dictionary),
    new AutocorrectionService(),
    converter);
var core = new PortableCorrectionEngine(decision, dictionary);
var nativeProvider = RustNativeShadowCandidateProvider.CreateFromEnvironment();
var scorer = new RustAdditionalCorrectionScorer(nativeProvider);
var audit = new AdditionalCorrectionScoringAuditService(core);
var hybridCheck = args.Any(argument =>
    string.Equals(argument, "--hybrid-check", StringComparison.OrdinalIgnoreCase));
var hybrid = new RustHybridCorrectionService(nativeProvider, converter, liveEnabled: hybridCheck);

var cases = new[]
{
    new AuditFixture("ghbdtn", true, true, "ru", "apply", "привет"),
    new AuditFixture("руддщ", true, true, "en", "apply", "hello"),
    new AuditFixture("превет", false, true, "ru", "apply", "привет"),
    new AuditFixture("стрвнно", false, true, "ru", "apply", "странно"),
    new AuditFixture("кмнда", false, true, "ru", "apply", "команда"),
    new AuditFixture("машына", false, true, "ru", "apply", "машина"),
    new AuditFixture("жызнь", false, true, "ru", "apply", "жизнь"),
    new AuditFixture("здрвствуйте", false, true, "ru", "apply", "здравствуйте"),
    new AuditFixture("првет", false, true, "ru", "apply", "привет"),
    new AuditFixture("teh", false, true, "en", "apply", "the"),
    new AuditFixture("adn", false, true, "en", "apply", "and"),
    new AuditFixture("helo", false, true, "en", "apply", "hello"),
    new AuditFixture("мирр", false, true, "ru", "wait", null),
    new AuditFixture("hello", true, true, "en", "no_change", null),
    new AuditFixture("привет", true, true, "ru", "no_change", null),
    new AuditFixture("camelCase", true, true, "en", "no_change", null),
    new AuditFixture("https://example.com/ghbdtn", true, true, "en", "no_change", null),
    new AuditFixture("дла", false, true, "ru", "apply", "для"),
    new AuditFixture("lfq", true, true, "ru", "apply", "дай"),
    new AuditFixture("rjvfyle", true, true, "ru", "apply", "команду"),
    new AuditFixture("pfgecrf", true, true, "ru", "apply", "запуска"),
};
var fixtureList = cases.ToList();
var fixtureSource = "synthetic";
if (args.Length >= 2 && string.Equals(args[0], "--held-out", StringComparison.OrdinalIgnoreCase))
{
    var heldOutPath = Path.GetFullPath(args[1]);
    fixtureList.AddRange(LoadHeldOut(heldOutPath));
    fixtureSource = $"synthetic+heldout:{CaseId(heldOutPath)}";
}

var counts = Enum.GetValues<AdditionalScorerComparisonOutcome>()
    .ToDictionary(value => value, _ => 0);
var coreDecisions = new Dictionary<string, int>(StringComparer.Ordinal);
var scorerReasons = new Dictionary<string, int>(StringComparer.Ordinal);
var coreFalseApply = 0;
var coreFalseKeep = 0;
var coreWrongTarget = 0;
var scorerFalseApply = 0;
var scorerFalseKeep = 0;
var scorerWrongTarget = 0;
var hybridApplied = 0;
var hybridFalseApply = 0;
var hybridFalseKeep = 0;
var hybridWrongTarget = 0;

foreach (var item in fixtureList)
{
    var request = new PortableCorrectionRequest
    {
        Token = item.Token,
        LayoutEnabled = item.Layout,
        AutocorrectEnabled = item.Autocorrect,
        DominantLanguage = item.Language,
        RussianTokenCount = item.Language == "ru" ? 3 : 0,
        EnglishTokenCount = item.Language == "en" ? 3 : 0,
        ContextTokenCount = 3,
        ContextCharacterCount = 18,
    };

    var result = audit.Compare(request, scorer);
    counts[result.Outcome]++;
    coreDecisions[result.CoreDecision] = coreDecisions.GetValueOrDefault(result.CoreDecision) + 1;
    scorerReasons[result.ScorerReasonCode] = scorerReasons.GetValueOrDefault(result.ScorerReasonCode) + 1;
    Console.WriteLine(
        $"Case id={CaseId(item.Token)} expected={item.ExpectedDecision} "
        + $"core={result.CoreDecision} scorer={result.ScorerDecision} "
        + $"reason={result.ScorerReasonCode} "
        + $"coreDigest={result.CoreReplacementDigest ?? "-"} "
        + $"scorerDigest={result.ScorerReplacementDigest ?? "-"}");
    Classify(result.CoreDecision, result.CoreReplacementDigest, item.ExpectedDecision, item.ExpectedReplacement,
        ref coreFalseApply, ref coreFalseKeep, ref coreWrongTarget);
    Classify(result.ScorerDecision, result.ScorerReplacementDigest, item.ExpectedDecision, item.ExpectedReplacement,
        ref scorerFalseApply, ref scorerFalseKeep, ref scorerWrongTarget);

    if (hybridCheck)
    {
        var hint = new SentenceLanguageHint(
            item.Language == "ru" ? TypingLanguage.Russian : TypingLanguage.English,
            item.Language == "ru" ? 3 : 0,
            item.Language == "en" ? 3 : 0,
            3,
            18);
        var coreDecision = decision.Evaluate(
            item.Token,
            dictionary,
            item.Layout,
            item.Autocorrect,
            new AutocorrectionOptions(),
            languageHint: hint);
        var hybridDecision = hybrid.TryCreateApprovedDecision(
            item.Token,
            coreDecision,
            dictionary,
            item.Layout,
            item.Autocorrect,
            new AutocorrectionOptions(),
            hint);
        var hybridDecisionName = hybridDecision?.Recommendation == JointCorrectionRecommendation.Apply
            ? "apply"
            : "no_change";
        var hybridDigest = hybridDecision?.ReplacementToken is null
            ? null
            : Digest(hybridDecision.ReplacementToken);
        var nativeForHybrid = nativeProvider.Evaluate(
            item.Token,
            RustShadowContextFormatter.FromPortableRequest(new PortableCorrectionRequest
            {
                Token = item.Token,
                DominantLanguage = item.Language,
                RussianTokenCount = item.Language == "ru" ? 3 : 0,
                EnglishTokenCount = item.Language == "en" ? 3 : 0,
                ContextTokenCount = 3,
                ContextCharacterCount = 18,
            }));
        if (hybridDecisionName == "apply")
        {
            hybridApplied++;
        }

        var expectedApply = item.ExpectedDecision == "apply";
        if ((expectedApply != (hybridDecisionName == "apply"))
            || (expectedApply
                && hybridDecisionName == "apply"
                && item.ExpectedReplacement is not null
                && !string.Equals(hybridDigest, Digest(item.ExpectedReplacement), StringComparison.Ordinal)))
        {
            var sourceKnown = dictionary.Contains(item.Token, TypingLanguage.Russian);
            var sourceKnownByForms = wordForms.IsKnownWord(item.Token, TypingLanguage.Russian);
            var candidateKnown = nativeForHybrid.ReplacementToken is not null
                && dictionary.Contains(nativeForHybrid.ReplacementToken, TypingLanguage.Russian);
            var candidateFrequency = nativeForHybrid.ReplacementToken is null
                ? 0.0
                : dictionary.GetFrequency(nativeForHybrid.ReplacementToken, TypingLanguage.Russian);
            Console.WriteLine(
                $"HybridMismatch case={CaseId(item.Token)} expected={item.ExpectedDecision} "
                + $"actual={hybridDecisionName} digest={hybridDigest ?? "-"} "
                + $"layout={item.Layout} autocorrect={item.Autocorrect} lang={item.Language} "
                + $"length={item.Token.Length} core={coreDecision.Recommendation} "
                + $"reason={nativeForHybrid.Reason} "
                + $"confidence={nativeForHybrid.Confidence:0.000} margin={nativeForHybrid.Margin:0.000} "
                + $"sourceKnown={sourceKnown} "
                + $"sourceKnownByForms={sourceKnownByForms} "
                + $"candidateLength={nativeForHybrid.ReplacementToken?.Length ?? 0} "
                + $"candidateKnown={candidateKnown} "
                + $"candidateFrequency={candidateFrequency:0.000} "
                + $"sourceShape={DescribeRussianShape(item.Token)} "
                + $"candidateShape={DescribeRussianShape(nativeForHybrid.ReplacementToken ?? string.Empty)}");
        }

        Classify(
            hybridDecisionName,
            hybridDigest,
            item.ExpectedDecision,
            item.ExpectedReplacement,
            ref hybridFalseApply,
            ref hybridFalseKeep,
            ref hybridWrongTarget);
    }
}

Console.WriteLine($"ScorerAudit cases={fixtureList.Count}; source={fixtureSource}");
Console.WriteLine("CoreDecisions " + string.Join(",", coreDecisions.OrderBy(pair => pair.Key)
    .Select(pair => $"{pair.Key}={pair.Value}")));
Console.WriteLine("Outcomes " + string.Join(",", counts.Where(pair => pair.Value > 0)
    .OrderBy(pair => pair.Key.ToString())
    .Select(pair => $"{pair.Key}={pair.Value}")));
Console.WriteLine($"CoreErrors false_apply={coreFalseApply},false_keep={coreFalseKeep},wrong_target={coreWrongTarget}");
Console.WriteLine($"ScorerErrors false_apply={scorerFalseApply},false_keep={scorerFalseKeep},wrong_target={scorerWrongTarget}");
Console.WriteLine("ScorerReasons " + string.Join(",", scorerReasons.OrderBy(pair => pair.Key)
    .Select(pair => $"{pair.Key}={pair.Value}")));
if (hybridCheck)
{
    Console.WriteLine(
        $"HybridLive cases={fixtureList.Count}; provider={nativeProvider.State}; "
        + $"approved={hybridApplied}; false_apply={hybridFalseApply}; "
        + $"false_keep={hybridFalseKeep}; wrong_target={hybridWrongTarget}; "
        + "send_input=false; rawTextLogged=false");
}
Console.WriteLine($"Provider={scorer.GetType().Name}; liveReplacement=false; rawTextLogged=false");
(nativeProvider as IDisposable)?.Dispose();

static void Classify(
    string actualDecision,
    string? actualReplacementDigest,
    string expectedDecision,
    string? expectedReplacement,
    ref int falseApply,
    ref int falseKeep,
    ref int wrongTarget)
{
    var expectedApply = expectedDecision == "apply";
    var actualApply = actualDecision == "apply";
    if (!expectedApply && actualApply)
    {
        falseApply++;
    }
    else if (expectedApply && !actualApply)
    {
        falseKeep++;
    }
    else if (expectedApply && actualApply && expectedReplacement is not null
        && !string.Equals(actualReplacementDigest, Digest(expectedReplacement), StringComparison.Ordinal))
    {
        wrongTarget++;
    }
}

static string Digest(string value) => Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static string CaseId(string value) => Digest(value)[..16];

static string DescribeRussianShape(string value)
{
    var hasRepeatedCharacter = value.Zip(value.Skip(1), static (left, right) => left == right).Any(static same => same);
    var endsSoftSign = value.EndsWith('ь') || value.EndsWith('ъ');
    var hasDigits = value.Any(char.IsDigit);
    return $"repeat={hasRepeatedCharacter},soft={endsSoftSign},digits={hasDigits}";
}

static IReadOnlyList<AuditFixture> LoadHeldOut(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("Held-out fixture file was not found.", path);
    }

    var fixtures = new List<AuditFixture>();
    foreach (var line in File.ReadLines(path))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            continue;
        }

        var fields = line.Split('\t');
        if (fields.Length < 4 || string.IsNullOrWhiteSpace(fields[0]))
        {
            continue;
        }

        var expected = fields[1] == "-" ? null : fields[1];
        var decision = fields[3] is "apply" or "wait" or "no_change"
            ? fields[3]
            : "wait";
        fixtures.Add(new AuditFixture(
            fields[0],
            false,
            true,
            fields[2],
            decision,
            expected));
    }

    return fixtures;
}

record AuditFixture(
    string Token,
    bool Layout,
    bool Autocorrect,
    string Language,
    string ExpectedDecision,
    string? ExpectedReplacement);

sealed class EmptyUserDictionaryStore : IUserAutocorrectDictionaryStore
{
    public IReadOnlyList<UserAutocorrectDictionaryEntry> Entries => Array.Empty<UserAutocorrectDictionaryEntry>();

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AddOrUpdateAsync(UserAutocorrectDictionaryEntry entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(string word, TypingLanguage language, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
