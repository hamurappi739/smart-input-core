using SmartInput.Core.Configuration;
using SmartInput.Core.Engines;
using SmartInput.Core.Models;
using System.Text;

namespace SmartInput.Core.Tests;

[Trait("Category", "FocusedCorrection")]
public class Residual51AnalysisTableTests
{
    [Fact]
    public void Write_Residual51_SyntheticTable_FromLatestReport()
    {
        var reportPath = Path.Combine(
            Path.GetTempPath(),
            "smartinput-r1-residual-report",
            "remaining-ambiguous-applied.txt");
        Assert.True(File.Exists(reportPath), $"Missing {reportPath}; run R1ResidualAnalysisReportTests first.");

        var lines = File.ReadAllLines(reportPath, Encoding.UTF8);
        var outDir = Path.Combine(Path.GetTempPath(), "smartinput-residual51-table");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "residual51-analysis.txt");

        if (lines.Length == 0)
        {
            File.WriteAllText(
                outPath,
                "caseId|op|lenTok|lenSel|lenComp|sameLength|oneEditComp|ultraWinner|cluster\n"
                + "# residual AmbiguousApplied = 0 after C1/C2/C3 + vowel stem-split rules\n",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(outDir, "cluster-summary.txt"),
                "0 residual_AA_solved\n",
                Encoding.UTF8);
            Console.WriteLine("residual AmbiguousApplied = 0");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            "caseId|op|lenTok|lenSel|lenComp|sameLength|oneEditComp|ultraWinner|cluster");

        static string Field(string line, string key)
        {
            var part = line.Split('|').FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.Ordinal));
            return part is null ? "" : part[(key.Length + 1)..];
        }

        foreach (var line in lines)
        {
            var caseId = Field(line, "caseId");
            var op = Field(line, "op");
            var lenTok = Field(line, "lenTok");
            var lenSel = Field(line, "lenCand");
            var lenComp = Field(line, "lenParent");
            var sameLength = string.Equals(lenSel, lenComp, StringComparison.Ordinal);
            var oneEdit = Field(line, "oneEditComp");
            var ultraWinner = Field(line, "inBand");
            var cluster = Field(line, "discardReason");

            sb.AppendLine(
                $"{caseId}|{op}|{lenTok}|{lenSel}|{lenComp}|{sameLength}|{oneEdit}|{ultraWinner}|{cluster}");
        }

        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        var byCluster = sb.ToString().Split('\n').Skip(1).Where(l => l.Length > 0)
            .GroupBy(l => l.Split('|').Last().Trim())
            .OrderByDescending(g => g.Count());
        var summary = string.Join("\n", byCluster.Select(g => $"{g.Count()} {g.Key}"));
        File.WriteAllText(Path.Combine(outDir, "cluster-summary.txt"), summary + "\n", Encoding.UTF8);
        Console.WriteLine(summary);
        Console.WriteLine($"wrote={outPath}");
        Assert.Equal(lines.Length, sb.ToString().Split('\n').Count(l => l.Contains('|', StringComparison.Ordinal)) - 1);
    }
}
