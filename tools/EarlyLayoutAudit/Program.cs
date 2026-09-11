using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartInput.Core.Dictionaries;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;

// All evaluation output is aggregate-only. Neither source nor candidate text is emitted.
var corpus = StarterAutocorrectLexicon.Entries.OrderBy(x => x.Key.Language)
    .ThenBy(x => x.Key.Word, StringComparer.Ordinal)
    .Select(x => (Language: x.Key.Language, Word: x.Key.Word, Weight: x.Value * x.Value)).ToArray();
int Split(string word) => SHA256.HashData(Encoding.UTF8.GetBytes(word.ToLowerInvariant()))[0] % 10;
var train = corpus.Where(x => Split(x.Word) < 8).ToArray();
var validation = corpus.Where(x => Split(x.Word) == 8).ToArray();
var test = corpus.Where(x => Split(x.Word) == 9).ToArray();
var watch = Stopwatch.StartNew();
var model = EarlyLayoutModel.Train(train);
var trainMs = watch.Elapsed.TotalMilliseconds;
var converter = new KeyboardLayoutConverter();
var choices = new[] { 1.5, 2.5, 3.5, 4.5, 5.5, 7.0 };
var calibration = choices.Select(margin => Measure(validation, new EarlyLayoutOptions { MinimumMargin = margin })).ToArray();
var selected = calibration.FirstOrDefault(x => x.FalseSwitches == 0)?.Margin ?? choices[^1];
var testResult = Measure(test, new EarlyLayoutOptions { MinimumMargin = selected });
var testEnglish = Measure(test.Where(x => x.Language == TypingLanguage.English).ToArray(), new EarlyLayoutOptions { MinimumMargin = selected });
var testRussian = Measure(test.Where(x => x.Language == TypingLanguage.Russian).ToArray(), new EarlyLayoutOptions { MinimumMargin = selected });

using var binary = new MemoryStream();
model.Save(binary);
binary.Position = 0;
var loaded = EarlyLayoutModel.Load(binary);
var roundTrip = test.All(x => Enumerable.Range(1, Math.Min(x.Word.Length, EarlyLayoutModel.MaximumPrefixLength))
    .All(length =>
    {
        var prefix = x.Word[..length];
        var wrong = converter.Convert(prefix, x.Language == TypingLanguage.English
            ? LayoutConversionDirection.EnglishToRussian : LayoutConversionDirection.RussianToEnglish);
        var opposite = x.Language == TypingLanguage.English ? TypingLanguage.Russian : TypingLanguage.English;
        return model.Evaluate(prefix, x.Language) == loaded.Evaluate(prefix, x.Language)
            && model.Evaluate(wrong, opposite) == loaded.Evaluate(wrong, opposite);
    }));
var benchOptions = new EarlyLayoutOptions { MinimumMargin = selected };
var benchCases = test.Where(x => x.Word.Length >= 4).GroupBy(x => x.Language)
    .SelectMany(group => group.Take(256)).SelectMany(x => new[]
{
    (Token: x.Word[..4], Language: x.Language),
    (Token: converter.Convert(x.Word[..4], x.Language == TypingLanguage.English
        ? LayoutConversionDirection.EnglishToRussian : LayoutConversionDirection.RussianToEnglish),
        Language: x.Language == TypingLanguage.English ? TypingLanguage.Russian : TypingLanguage.English),
}).ToArray();
for (var i = 0; i < 10_000; i++)
    _ = model.Evaluate(benchCases[i % benchCases.Length].Token, benchCases[i % benchCases.Length].Language, benchOptions);
var ticks = new long[100_000];
var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
watch.Restart();
for (var i = 0; i < 100_000; i++)
{
    var item = benchCases[i % benchCases.Length];
    var start = Stopwatch.GetTimestamp();
    _ = model.Evaluate(item.Token, item.Language, benchOptions);
    ticks[i] = Stopwatch.GetTimestamp() - start;
}
watch.Stop();
var allocationBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
Array.Sort(ticks);

// Unknown identifiers and future URL suffixes cannot be distinguished from
// wrong-layout prose by the first four keys. Keep this adversarial test separate
// from the dictionary-based estimate rather than hiding it in that denominator.
var technicalPrefixes = new[] { "ghbd_id", "ghbd.example", "ghbd@example.test", "ghbd123", "ghbdCustom" };
var adversarialSwitches = technicalPrefixes.Count(token => Enumerable.Range(4, Math.Min(token.Length, 10) - 3)
    .Any(length => model.Evaluate(token[..length], TypingLanguage.English,
        new EarlyLayoutOptions { MinimumMargin = selected }).Verdict == EarlyLayoutVerdict.Candidate));
var report = JsonSerializer.Serialize(new
{
    Schema = 1, LiveReplacement = false, RawTextLogged = false,
    TrainingWords = train.Length, ValidationWords = validation.Length, TestWords = test.Length,
    Split = "normalized-word-sha256-before-prefix-expansion; not lemma/disjoint-domain heldout",
    Weighting = "squared rank scores; not corpus occurrence counts",
    TrainingMilliseconds = Math.Round(trainMs, 2), ModelTableBytes = model.TableBytes,
    BinaryBytes = binary.Length,
    ModelSha256 = Convert.ToHexString(SHA256.HashData(binary.ToArray())),
    RoundTrip = roundTrip, Calibration = calibration, SelectedMargin = selected, Test = testResult,
    English = testEnglish, Russian = testRussian,
    AdversarialFutureSuffix = new { Cases = technicalPrefixes.Length, FalseSwitches = adversarialSwitches },
    Benchmark = new { Calls = 100_000, SyntheticPrefixes = benchCases.Length,
        AverageMicroseconds = watch.Elapsed.TotalMicroseconds / 100_000,
        P95Microseconds = ticks[94999] * 1_000_000.0 / Stopwatch.Frequency,
        P99Microseconds = ticks[98999] * 1_000_000.0 / Stopwatch.Frequency,
        MaxMicroseconds = ticks[^1] * 1_000_000.0 / Stopwatch.Frequency,
        AllocatedBytesPerCall = allocationBytes / 100_000.0,
        Scope = "Evaluate only; balanced intended RU/EN; excludes session, hook, layout change and injection" },
    // Zero false switches in this corpus is necessary, not sufficient, for real-app activation.
    LiveReady = false,
}, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(report);
for (var arg = 0; arg < args.Length; arg += 2)
{
    if (arg + 1 >= args.Length || args[arg] is not ("--model-output" or "--report-output"))
        throw new ArgumentException("Expected --model-output PATH or --report-output PATH.");
    using var output = new FileStream(args[arg + 1], FileMode.CreateNew, FileAccess.Write);
    if (args[arg] == "--model-output")
    {
        binary.Position = 0;
        binary.CopyTo(output);
    }
    else
    {
        using var writer = new StreamWriter(output);
        writer.Write(report);
    }
}
return roundTrip ? 0 : 1;

AuditCounts Measure((TypingLanguage Language, string Word, double Weight)[] words, EarlyLayoutOptions options)
{
    var falseSwitches = 0;
    var recovered = 0;
    var beforeWordEnd = 0;
    var eligible = 0;
    var chars = 0;
    var byLength = new int[EarlyLayoutModel.MaximumPrefixLength + 1];
    foreach (var item in words)
    {
        if (item.Word.Length < options.MinimumLength) continue;
        eligible++;
        var wrongLanguage = item.Language == TypingLanguage.English ? TypingLanguage.Russian : TypingLanguage.English;
        var wrong = converter.Convert(item.Word, item.Language == TypingLanguage.English
            ? LayoutConversionDirection.EnglishToRussian : LayoutConversionDirection.RussianToEnglish);
        bool falseFound = false, recoveredFound = false;
        for (var length = options.MinimumLength; length <= Math.Min(item.Word.Length, EarlyLayoutModel.MaximumPrefixLength); length++)
        {
            chars++;
            if (!falseFound && model.Evaluate(item.Word[..length], item.Language, options).Verdict == EarlyLayoutVerdict.Candidate)
            {
                falseSwitches++;
                falseFound = true;
            }
            if (!recoveredFound && model.Evaluate(wrong[..length], wrongLanguage, options).Verdict == EarlyLayoutVerdict.Candidate)
            {
                recovered++;
                if (length < item.Word.Length) beforeWordEnd++;
                recoveredFound = true;
                byLength[length]++;
            }
        }
    }
    return new(options.MinimumMargin, eligible, chars, falseSwitches, recovered, eligible - recovered, beforeWordEnd, byLength);
}

sealed record AuditCounts(double Margin, int EligibleWords, int PrefixPositions,
    int FalseSwitches, int RecoveredWords, int MissedWords, int RecoveredBeforeWordEnd, int[] FirstSwitchLengthCounts);
