using System.Text;
using SmartInput.Core.Engines;

namespace SmartInput.Core.Tests;

/// <summary>
/// Developer-only synthetic corpus failure dump. Raw tokens are for local human analysis only.
/// </summary>
[Trait("Category", "FocusedCorrection")]
public class SyntheticCorpusFailureExampleReportTests
{
    [Fact]
    public void Write_Seed42_SyntheticFailureExampleReport()
    {
        var report = MaximumCorpusAuditHarness.RunFullAudit(
            seed: 42,
            minRussianMutations: 12_000,
            minEnglishMutations: 12_000);

        var outDir = Path.Combine(Path.GetTempPath(), "smartinput-synthetic-failure-report");
        Directory.CreateDirectory(outDir);
        var aaPath = Path.Combine(outDir, "seed42-ambiguous-applied-all.txt");
        var wlPath = Path.Combine(outDir, "seed42-wrong-direct-layout-all.txt");
        var humanWlPath = Path.Combine(outDir, "seed42-wl-human.txt");
        var summaryPath = Path.Combine(outDir, "seed42-summary.txt");

        File.WriteAllLines(
            aaPath,
            report.DeveloperSyntheticAmbiguousAppliedExamples.Select(SanitizeAa),
            Encoding.UTF8);
        File.WriteAllLines(
            wlPath,
            report.DeveloperSyntheticLayoutPairFailures.Select(SanitizeWl),
            Encoding.UTF8);
        File.WriteAllLines(
            humanWlPath,
            report.DeveloperSyntheticLayoutPairFailures
                .Select(ParseWl)
                .Select(FormatWlHuman),
            Encoding.UTF8);

        var aaByOp = report.DeveloperSyntheticAmbiguousAppliedExamples
            .Select(ParseAa)
            .GroupBy(x => x.Op)
            .OrderByDescending(g => g.Count())
            .ToList();
        var aaByExpected = report.DeveloperSyntheticAmbiguousAppliedExamples
            .Select(ParseAa)
            .GroupBy(x => x.Expected)
            .OrderByDescending(g => g.Count())
            .ToList();
        var aaCredible = report.DeveloperSyntheticAmbiguousAppliedExamples
            .Select(ParseAa)
            .GroupBy(x => x.IntendedCredible)
            .ToList();

        var wlByPolicy = report.DeveloperSyntheticLayoutPairFailures
            .Select(ParseWl)
            .GroupBy(x => x.Policy)
            .OrderByDescending(g => g.Count())
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"seed=42 AA={report.TotalAmbiguousApplied} WU={report.WrongUniqueTarget} WL={report.WrongDirectLayout}");
        sb.AppendLine($"AA_examples={report.DeveloperSyntheticAmbiguousAppliedExamples.Count}");
        sb.AppendLine($"WL_examples={report.DeveloperSyntheticLayoutPairFailures.Count}");
        sb.AppendLine($"MustPreserveApplied={report.MustPreserveApplied}; MustWaitApplied={report.MustWaitApplied}; PairHarness={report.WrongDirectLayoutPairHarness}; Mutation={report.WrongDirectLayoutMutation}");
        sb.AppendLine($"recovery={report.RecoveryRate:P2}; peak={report.PeakWorkingSetBytes}");
        sb.AppendLine();
        sb.AppendLine("AA by operation:");
        foreach (var g in aaByOp)
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("AA by expectedVerdict:");
        foreach (var g in aaByExpected)
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("AA by intendedCredible:");
        foreach (var g in aaCredible)
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("WL by policy:");
        foreach (var g in wlByPolicy)
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine();
        sb.AppendLine("=== AA samples (20 per top operations) ===");
        foreach (var g in aaByOp.Take(8))
        {
            sb.AppendLine($"-- op={g.Key} n={g.Count()} --");
            foreach (var row in g.Take(20))
            {
                sb.AppendLine(FormatAaHuman(row));
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== WL samples (20 per policy) ===");
        foreach (var g in wlByPolicy)
        {
            sb.AppendLine($"-- policy={g.Key} n={g.Count()} --");
            foreach (var row in g.Take(20))
            {
                sb.AppendLine(FormatWlHuman(row));
            }
        }

        // Also dump ShouldConvert wrong-target pattern: actual vs expected script/freq
        sb.AppendLine();
        sb.AppendLine("=== ShouldConvert wrong-target pattern (top 40) ===");
        foreach (var row in report.DeveloperSyntheticLayoutPairFailures.Select(ParseWl)
                     .Where(x => x.Policy == "ShouldConvert")
                     .Take(40))
        {
            sb.AppendLine(FormatWlHuman(row));
        }

        File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"artifacts: {outDir}");

        // Soft assert so the dump always runs; acceptance is separate.
        Assert.True(
            report.DeveloperSyntheticAmbiguousAppliedExamples.Count > 0
            || report.TotalAmbiguousApplied == 0,
            $"AA={report.TotalAmbiguousApplied} examples={report.DeveloperSyntheticAmbiguousAppliedExamples.Count}; summary={summaryPath}");
        Assert.True(
            report.DeveloperSyntheticLayoutPairFailures.Count > 0
            || report.WrongDirectLayout == 0,
            $"WL={report.WrongDirectLayout} examples={report.DeveloperSyntheticLayoutPairFailures.Count}; summary={summaryPath}");
    }

    private static AaRow ParseAa(string line)
    {
        string Get(string key)
        {
            var part = line.Split('|').FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.Ordinal));
            return part is null ? "" : part[(key.Length + 1)..];
        }

        return new AaRow(
            Get("caseId"),
            Get("token"),
            Get("prod"),
            Get("intended"),
            Get("op"),
            Get("competitors"),
            Get("productionVerdict"),
            Get("expectedVerdict"),
            Get("stage"),
            Get("intendedCredible"),
            Get("discarded"),
            Get("shouldRemap"),
            Get("reason"));
    }

    private static WlRow ParseWl(string line)
    {
        string Get(string key)
        {
            var part = line.Split('|').FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.Ordinal));
            return part is null ? "" : part[(key.Length + 1)..];
        }

        return new WlRow(
            Get("caseId"),
            Get("typed"),
            Get("expected"),
            Get("actual"),
            Get("policy"),
            Get("expectedVerdict"),
            Get("actualVerdict"),
            Get("spellClass"),
            Get("spellUniq"),
            Get("harness"),
            Get("typedFreq"),
            Get("targetFreq"));
    }

    private static string FormatAaHuman(AaRow row) =>
        $"id={row.CaseId} op={row.Op} "
        + $"verdict={row.ProductionVerdict} expected={row.Expected} "
        + $"stage={row.Stage} credible={row.IntendedCredible} discarded={row.Discarded} remap={row.ShouldRemap} reason={row.Reason}";

    private static string FormatWlHuman(WlRow row) =>
        $"id={row.CaseId} policy={row.Policy} "
        + $"expectedVerdict={row.ExpectedVerdict} actualVerdict={row.ActualVerdict} spellClass={row.SpellClass} "
        + $"harness={row.Harness} typedFreq={row.TypedFreq} targetFreq={row.TargetFreq}";

    private static string SanitizeAa(string line)
    {
        var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "caseId", "op", "productionVerdict", "expectedVerdict", "stage", "intendedCredible",
            "discarded", "shouldRemap", "reason", "parentIsIntended", "inBand", "discardReason",
            "parentOp", "candOp", "lenParent", "lenCand", "lenTok", "oneEditComp", "blockingRule"
        };

        return string.Join(
            '|',
            line.Split('|').Where(part =>
            {
                var separator = part.IndexOf('=');
                return separator > 0 && allowedKeys.Contains(part[..separator]);
            }));
    }

    private static string SanitizeWl(string line)
    {
        var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "caseId", "policy", "expectedVerdict", "actualVerdict", "spellClass", "harness",
            "typedFreq", "targetFreq", "typedLang", "targetLang", "typedAlsoInTarget",
            "srcScript", "tgtScript", "stage"
        };

        return string.Join(
            '|',
            line.Split('|').Where(part =>
            {
                var separator = part.IndexOf('=');
                return separator > 0 && allowedKeys.Contains(part[..separator]);
            }));
    }

    private sealed record AaRow(
        string CaseId,
        string Token,
        string Prod,
        string Intended,
        string Op,
        string Competitors,
        string ProductionVerdict,
        string Expected,
        string Stage,
        string IntendedCredible,
        string Discarded,
        string ShouldRemap,
        string Reason);

    private sealed record WlRow(
        string CaseId,
        string Typed,
        string Expected,
        string Actual,
        string Policy,
        string ExpectedVerdict,
        string ActualVerdict,
        string SpellClass,
        string SpellUniq,
        string Harness,
        string TypedFreq,
        string TargetFreq);
}
