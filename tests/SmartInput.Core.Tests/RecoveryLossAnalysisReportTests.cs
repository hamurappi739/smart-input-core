using System.Globalization;
using System.Text;
using SmartInput.Core.Engines;

namespace SmartInput.Core.Tests;

/// <summary>
/// Privacy-safe Unique==intended Wait recovery-loss report (hashed caseId + local synthetic fields).
/// </summary>
[Trait("Category", "FocusedCorrection")]
public class RecoveryLossAnalysisReportTests
{
    private const double SafetyRecoveryFloor = 0.90;

    [Fact]
    public void Write_Seed42_UniqueIntendedWait_ByBlockingRule()
    {
        var report = MaximumCorpusAuditHarness.RunFullAudit(
            seed: 42,
            minRussianMutations: 12_000,
            minEnglishMutations: 12_000);

        var outDir = Path.Combine(Path.GetTempPath(), "smartinput-recovery-loss-report");
        Directory.CreateDirectory(outDir);

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

        static double Freq(string line, string key)
        {
            var raw = Field(line, key).Replace(',', '.');
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        var waits = report.DeveloperSyntheticR1ForcedWaitExamples;
        File.WriteAllLines(
            Path.Combine(outDir, "all-unique-intended-waits.txt"),
            waits.Select(Sanitize),
            Encoding.UTF8);

        var summary = new StringBuilder();
        summary.AppendLine($"AA={report.TotalAmbiguousApplied}");
        summary.AppendLine($"WU={report.WrongUniqueTarget} WL={report.WrongDirectLayout} WC={report.WrongCombined} ExactChanged={report.ExactWordsChanged}");
        summary.AppendLine($"recovery={report.RecoveryRate:P4} eligible={report.EligibleUnambiguousMutations} recoveries={report.CorrectMutationRecoveries}");
        summary.AppendLine($"missedApprox={report.EligibleUnambiguousMutations - report.CorrectMutationRecoveries}");
        summary.AppendLine($"UniqueIntendedWaitExamples={waits.Count}");
        summary.AppendLine($"NonR1UniqueIntendedWaitExamples={report.DeveloperSyntheticNonR1ForcedWaitExamples.Count}");
        summary.AppendLine();
        summary.AppendLine("WaitedUnique terminal=" +
            report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.WaitedUnique));
        summary.AppendLine("NoCandidateUnique terminal=" +
            report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.NoCandidateUnique));
        summary.AppendLine("ProtectedUnique terminal=" +
            report.UniqueTerminalCounts.GetValueOrDefault(UniqueMutationTerminal.ProtectedUnique));
        summary.AppendLine();
        summary.AppendLine("by blockingRule:");
        foreach (var g in waits.GroupBy(l => Field(l, "blockingRule")).OrderByDescending(g => g.Count()))
        {
            summary.AppendLine($"  {g.Key}={g.Count()}");
        }

        summary.AppendLine();
        summary.AppendLine("by oneEditComp:");
        foreach (var g in waits.GroupBy(l => Field(l, "oneEditComp")).OrderByDescending(g => g.Count()))
        {
            summary.AppendLine($"  {g.Key}={g.Count()}");
        }

        // Freable heuristic candidates (analysis only — not applied automatically).
        var decisiveMargin = waits.Where(l =>
        {
            var cand = Freq(l, "candFreq");
            var comp = Freq(l, "compFreq");
            return cand >= comp + 0.12 && cand >= 0.95 && Field(l, "oneEditComp") == "False";
        }).ToList();
        var outOfBand = waits.Where(l => Field(l, "oneEditComp") == "False" || Field(l, "inBand") == "False").ToList();
        summary.AppendLine();
        summary.AppendLine($"heuristic_outOfBand_or_notOneEdit={outOfBand.Count}");
        summary.AppendLine($"heuristic_decisiveMargin_and_notOneEdit={decisiveMargin.Count}");
        summary.AppendLine();
        summary.AppendLine("outOfBand by blockingRule:");
        foreach (var g in outOfBand.GroupBy(l => Field(l, "blockingRule")).OrderByDescending(g => g.Count()))
        {
            summary.AppendLine($"  {g.Key}={g.Count()}");
        }

        summary.AppendLine();
        summary.AppendLine("=== NonR1 Wait sample 40 ===");
        foreach (var line in report.DeveloperSyntheticNonR1ForcedWaitExamples.Take(40))
        {
            summary.AppendLine(Sanitize(line));
        }

        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString(), Encoding.UTF8);
        File.WriteAllLines(
            Path.Combine(outDir, "non-r1-unique-intended-waits.txt"),
            report.DeveloperSyntheticNonR1ForcedWaitExamples.Select(Sanitize),
            Encoding.UTF8);

        foreach (var g in waits.GroupBy(l => Field(l, "blockingRule")))
        {
            var safeName = string.Join("_", g.Key.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllLines(
                Path.Combine(outDir, $"group-{safeName}.txt"),
                g.Select(Sanitize),
                Encoding.UTF8);
        }

        Console.WriteLine(summary.ToString());
        Console.WriteLine($"artifacts={outDir}");
        Console.WriteLine($"AmbiguousApplied={report.TotalAmbiguousApplied}");
        Console.WriteLine($"WrongUniqueTarget={report.WrongUniqueTarget}");
        Console.WriteLine($"WrongDirectLayout={report.WrongDirectLayout}");
        Console.WriteLine($"WrongCombined={report.WrongCombined}");
        Console.WriteLine($"ExactWordsChanged={report.ExactWordsChanged}");
        Console.WriteLine($"unique recovery={report.RecoveryRate:P4} ({report.CorrectMutationRecoveries}/{report.EligibleUnambiguousMutations})");

        Assert.Equal(0, report.TotalAmbiguousApplied);
        Assert.Equal(0, report.WrongUniqueTarget);
        Assert.Equal(0, report.WrongDirectLayout);
        Assert.Equal(0, report.WrongCombined);
        Assert.Equal(0, report.ExactWordsChanged);
        Assert.True(
            report.RecoveryRate >= SafetyRecoveryFloor,
            $"unique recovery={report.RecoveryRate:P4} need>={SafetyRecoveryFloor:P0}; "
            + "95% remains the optimization target; "
            + $"recoveries={report.CorrectMutationRecoveries}/{report.EligibleUnambiguousMutations}");
    }
}
