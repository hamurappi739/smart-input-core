using System.Text;
using SmartInput.Core.Engines;

namespace SmartInput.Core.Tests;

/// <summary>
/// Privacy-safe synthetic R1 residual reports (hashed caseId; synthetic tokens for local analysis only).
/// </summary>
[Trait("Category", "FocusedCorrection")]
public class R1ResidualAnalysisReportTests
{
    [Fact]
    public void Write_Seed42_R1_ResidualAndRecoveryLossReports()
    {
        var report = MaximumCorpusAuditHarness.RunFullAudit(
            seed: 42,
            minRussianMutations: 12_000,
            minEnglishMutations: 12_000);

        var outDir = Path.Combine(Path.GetTempPath(), "smartinput-r1-residual-report");
        Directory.CreateDirectory(outDir);
        var aaPath = Path.Combine(outDir, "remaining-ambiguous-applied.txt");
        var waitPath = Path.Combine(outDir, "r1-forced-wait-unique.txt");
        var summaryPath = Path.Combine(outDir, "summary.txt");

        File.WriteAllLines(
            aaPath,
            report.DeveloperSyntheticAmbiguousAppliedExamples.Select(Sanitize),
            Encoding.UTF8);
        File.WriteAllLines(
            waitPath,
            report.DeveloperSyntheticR1ForcedWaitExamples.Select(Sanitize),
            Encoding.UTF8);

        static string Field(string line, string key)
        {
            var part = line.Split('|').FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.Ordinal));
            return part is null ? "" : part[(key.Length + 1)..];
        }

        static string Sanitize(string line)
        {
            var rawValueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "token", "selected", "intended", "longerParent", "competitor", "parent",
                "cand", "uniq", "uniqueTarget"
            };

            return string.Join(
                '|',
                line.Split('|').Where(part =>
                {
                    var separator = part.IndexOf('=');
                    return separator > 0 && !rawValueKeys.Contains(part[..separator]);
                }));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"AA={report.TotalAmbiguousApplied} examples={report.DeveloperSyntheticAmbiguousAppliedExamples.Count}");
        sb.AppendLine($"WU={report.WrongUniqueTarget} WL={report.WrongDirectLayout} WC={report.WrongCombined} ExactChanged={report.ExactWordsChanged}");
        sb.AppendLine($"recovery={report.RecoveryRate:P2} eligible={report.EligibleUnambiguousMutations} recoveries={report.CorrectMutationRecoveries}");
        sb.AppendLine($"R1WaitExamples={report.DeveloperSyntheticR1ForcedWaitExamples.Count}");
        sb.AppendLine();
        sb.AppendLine("AA discardReason:");
        foreach (var g in report.DeveloperSyntheticAmbiguousAppliedExamples
                     .GroupBy(l => Field(l, "discardReason"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("AA parentIsIntended:");
        foreach (var g in report.DeveloperSyntheticAmbiguousAppliedExamples
                     .GroupBy(l => Field(l, "parentIsIntended"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("AA op:");
        foreach (var g in report.DeveloperSyntheticAmbiguousAppliedExamples
                     .GroupBy(l => Field(l, "op"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("AA parentOp:");
        foreach (var g in report.DeveloperSyntheticAmbiguousAppliedExamples
                     .GroupBy(l => Field(l, "parentOp"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("AA length relation (tok/cand/parent):");
        foreach (var g in report.DeveloperSyntheticAmbiguousAppliedExamples
                     .GroupBy(l =>
                     {
                         _ = int.TryParse(Field(l, "lenTok"), out var t);
                         _ = int.TryParse(Field(l, "lenCand"), out var c);
                         _ = int.TryParse(Field(l, "lenParent"), out var p);
                         if (p > c)
                         {
                             return "parent_longer";
                         }

                         if (p == c)
                         {
                             return "same_length";
                         }

                         return "parent_shorter";
                     })
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("AA inBand:");
        foreach (var g in report.DeveloperSyntheticAmbiguousAppliedExamples
                     .GroupBy(l => Field(l, "inBand"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine();
        sb.AppendLine("R1Wait discardReason:");
        foreach (var g in report.DeveloperSyntheticR1ForcedWaitExamples
                     .GroupBy(l => Field(l, "discardReason"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("R1Wait parentIsIntended:");
        foreach (var g in report.DeveloperSyntheticR1ForcedWaitExamples
                     .GroupBy(l => Field(l, "parentIsIntended"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine("R1Wait op:");
        foreach (var g in report.DeveloperSyntheticR1ForcedWaitExamples
                     .GroupBy(l => Field(l, "op"))
                     .OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {g.Key}={g.Count()}");
        }

        sb.AppendLine();
        sb.AppendLine("=== AA sample 40 ===");
        foreach (var line in report.DeveloperSyntheticAmbiguousAppliedExamples.Take(40))
        {
            sb.AppendLine(Sanitize(line));
        }

        sb.AppendLine();
        sb.AppendLine("=== R1 Wait sample 40 ===");
        foreach (var line in report.DeveloperSyntheticR1ForcedWaitExamples.Take(40))
        {
            sb.AppendLine(Sanitize(line));
        }

        File.WriteAllText(summaryPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine(sb.ToString());
        Console.WriteLine($"artifacts={outDir}");

        Assert.True(
            report.DeveloperSyntheticAmbiguousAppliedExamples.Count == report.TotalAmbiguousApplied
            || report.TotalAmbiguousApplied == 0,
            $"AA mismatch examples={report.DeveloperSyntheticAmbiguousAppliedExamples.Count} counter={report.TotalAmbiguousApplied}");
        Assert.True(
            report.DeveloperSyntheticR1ForcedWaitExamples.Count >= 200
            || report.RecoveryRate >= 0.95,
            $"Need >=200 R1 wait samples for analysis; got {report.DeveloperSyntheticR1ForcedWaitExamples.Count}; summary={summaryPath}");
    }
}
