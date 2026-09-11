using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SmartInput.Infrastructure.RecoveredRules;

namespace SmartInput.Core.Tests;

public sealed class RecoveredRulePackTests
{
    [Fact]
    public void AuditPipeline_ProducesProvedMatcherBytesButNeverApplyDecision()
    {
        var pipeline = new RecoveredAuditPipeline();

        var observation = pipeline.ObserveBoundary("ghbdtn "u8);

        Assert.Equal(RecoveredBoundaryPreparationStatus.PreparedKnownWhitespace, observation.Status);
        Assert.Equal(0x81D538, observation.DescriptorRva);
        Assert.Equal(6, observation.TrimmedByteLength);
        Assert.Equal(RecoveredAuditDecision.Wait, observation.Decision);
        Assert.True(observation.HasFinalMatcherBytes);
        Assert.Equal(new byte[] { 0x02, (byte)'g', (byte)'h', (byte)'b', (byte)'d', (byte)'t', (byte)'n', 0x03 },
            observation.CopyFinalMatcherBytes());
        Assert.False(observation.CanApply);
    }

    [Fact]
    public void Load_VerifiesAutomataAndResolvesOpaqueRules()
    {
        using var fixture = new RecoveredRuleFixture();

        var pack = RecoveredRulePack.Load(fixture.IntegrationPath);
        var audit = pack.Match(RecoveredAutomatonKind.A, new byte[] { 1 });

        Assert.Equal(4, pack.AutomatonARecordCount);
        Assert.Equal(1, pack.AutomatonARuleCount);
        Assert.Equal("test-1", pack.ModelVersion);
        Assert.Equal(RecoveredRuleMatchStatus.CompletePath, audit.Status);
        var match = Assert.Single(audit.Matches);
        Assert.Equal(7, match.OutputId);
        Assert.Equal(RecoveredAutomatonKind.A, match.Source);
        Assert.True(pack.TryResolveRule(match.Source, match.OutputId, out var rule));
        Assert.NotNull(rule);
        Assert.Equal(7, rule.OutputId);
        Assert.Equal(new byte[] { 0x08, 0x07, 0x12, 0x00 }, rule.CopyRawRecord());

        var bindings = pack.AuditOpaqueBindings();
        Assert.True(bindings.IsExact);
        Assert.Equal(new[] { 7 }, bindings.AutomatonA.OutputIds);
        Assert.Equal(new[] { 0 }, bindings.AutomatonB.OutputIds);
    }

    [Fact]
    public void Match_BReturnsIndexedOpaqueRule_AndNeverReplacement()
    {
        using var fixture = new RecoveredRuleFixture();
        var pack = RecoveredRulePack.Load(fixture.IntegrationPath);

        var audit = pack.Match(RecoveredAutomatonKind.B, new byte[] { 1 });

        var match = Assert.Single(audit.Matches);
        Assert.Equal(0, match.OutputId);
        Assert.True(pack.TryResolveRule(RecoveredAutomatonKind.B, 0, out var rule));
        Assert.NotNull(rule);
        Assert.Equal(new byte[] { 0x18, 0x01 }, rule.CopyRawRecord());
        Assert.True(pack.TryResolveBLinkedRecord(0, out var linkedIndex, out var output, out var linked));
        Assert.Equal(1, linkedIndex);
        Assert.NotNull(output);
        Assert.NotNull(linked);
        Assert.Equal(new byte[] { 0x18, 0x01 }, output.CopyRawRecord());
        Assert.Equal(new byte[] { 0x2A }, linked.CopyRawRecord());
        Assert.False(pack.TryResolveRule(RecoveredAutomatonKind.B, 1, out _));
    }

    [Fact]
    public void Match_IncompletePath_ReturnsOnlyAuditStatus()
    {
        using var fixture = new RecoveredRuleFixture();
        var pack = RecoveredRulePack.Load(fixture.IntegrationPath);

        var audit = pack.Match(RecoveredAutomatonKind.A, new byte[] { 2 });

        Assert.Equal(RecoveredRuleMatchStatus.IncompletePath, audit.Status);
        Assert.Equal(0, audit.ProcessedByteCount);
        Assert.Empty(audit.Matches);
    }

    [Fact]
    public void MatchSubstrings_RestartsAtEveryOffsetAndSupportsBothDirections()
    {
        using var fixture = new RecoveredRuleFixture();
        var pack = RecoveredRulePack.Load(fixture.IntegrationPath);

        var forward = pack.MatchSubstrings(
            RecoveredAutomatonKind.A,
            RecoveredMatchDirection.Forward,
            new byte[] { 9, 1, 9, 1 });
        var reverse = pack.MatchSubstrings(
            RecoveredAutomatonKind.A,
            RecoveredMatchDirection.Reverse,
            new byte[] { 1, 9, 1 });

        Assert.Equal(RecoveredRuleMatchStatus.SubstringScanCompleted, forward.Status);
        Assert.Equal(RecoveredRuleMatchStatus.SubstringScanCompleted, reverse.Status);
        Assert.All(forward.Matches, match => Assert.Equal(7, match.OutputId));
        Assert.All(reverse.Matches, match => Assert.Equal(7, match.OutputId));
        Assert.Equal(4, forward.ProcessedByteCount);
        Assert.Equal(3, reverse.ProcessedByteCount);
    }

    [Fact]
    public void Load_TamperedAutomaton_IsRejectedBeforeParsing()
    {
        using var fixture = new RecoveredRuleFixture();
        File.WriteAllBytes(Path.Combine(fixture.IntegrationPath, "dawg", "lexicon-a.dawg"), [1, 2, 3]);

        Assert.Throws<CryptographicException>(() => RecoveredRulePack.Load(fixture.IntegrationPath));
    }

    private sealed class RecoveredRuleFixture : IDisposable
    {
        public RecoveredRuleFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "smartinput-recovered-" + Guid.NewGuid().ToString("N"));
            IntegrationPath = Path.Combine(RootPath, "integration");
            Directory.CreateDirectory(Path.Combine(IntegrationPath, "dawg"));
            Directory.CreateDirectory(Path.Combine(IntegrationPath, "rules"));
            Directory.CreateDirectory(Path.Combine(IntegrationPath, "auxiliary"));

            var blockA = BuildOutputBlock(outputId: 7);
            var blockB = BuildOutputBlock(outputId: 0);
            var aPath = Path.Combine(IntegrationPath, "dawg", "lexicon-a.dawg");
            var bPath = Path.Combine(IntegrationPath, "dawg", "lexicon-b.dawg");
            File.WriteAllBytes(aPath, blockA);
            File.WriteAllBytes(bPath, blockB);
            File.WriteAllBytes(Path.Combine(IntegrationPath, "auxiliary", "group-3-field-2.bin"), [0x01]);

            WriteJsonLines(Path.Combine(IntegrationPath, "rules", "group-2-field-3.jsonl"),
                [0x08, 0x07, 0x12, 0x00]);
            WriteJsonLines(Path.Combine(IntegrationPath, "rules", "group-2-field-4.jsonl"));
            WriteJsonLines(Path.Combine(IntegrationPath, "rules", "group-3-field-3.jsonl"));
            WriteJsonLines(
                Path.Combine(IntegrationPath, "rules", "group-5-field-2.jsonl"),
                [0x18, 0x01]);
            WriteJsonLines(Path.Combine(IntegrationPath, "rules", "group-5-field-3.jsonl"), [], [0x2A]);

            var aHash = Convert.ToHexString(SHA256.HashData(blockA));
            var bHash = Convert.ToHexString(SHA256.HashData(blockB));
            File.WriteAllText(Path.Combine(RootPath, "hashes.sha256"),
                $"{aHash} *integration/dawg/lexicon-a.dawg{Environment.NewLine}{bHash} *integration/dawg/lexicon-b.dawg{Environment.NewLine}");
            File.WriteAllText(Path.Combine(IntegrationPath, "model-blocks-manifest.json"), CreateManifest(aHash, bHash));
        }

        public string RootPath { get; }
        public string IntegrationPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static byte[] BuildOutputBlock(uint outputId)
        {
            var records = new uint[4];
            records[0] = 1;
            records[1] = 1u | 0x100u | (2u << 10);
            records[3] = outputId;
            var block = new byte[(records.Length + 1) * sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(block, checked((uint)records.Length));
            for (var index = 0; index < records.Length; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    block.AsSpan((index + 1) * sizeof(uint)),
                    records[index]);
            }

            return block;
        }

        private static void WriteJsonLines(string path, params byte[][] records)
        {
            var lines = records.Select(record => JsonSerializer.Serialize(new
            {
                raw_base64 = Convert.ToBase64String(record),
            }));
            File.WriteAllText(path, string.Join(Environment.NewLine, lines));
        }

        private static string CreateManifest(string aHash, string bHash)
        {
            var artifacts = new Dictionary<string, object>
            {
                ["dawg/lexicon-a.dawg"] = new Dictionary<string, object> { ["sha256"] = aHash },
                ["dawg/lexicon-b.dawg"] = new Dictionary<string, object> { ["sha256"] = bHash },
                ["rules/group-2-field-3.jsonl"] = new Dictionary<string, object> { ["records"] = 1 },
                ["rules/group-2-field-4.jsonl"] = new Dictionary<string, object> { ["records"] = 0 },
                ["rules/group-3-field-3.jsonl"] = new Dictionary<string, object> { ["records"] = 0 },
                ["rules/group-5-field-2.jsonl"] = new Dictionary<string, object> { ["records"] = 1 },
                ["rules/group-5-field-3.jsonl"] = new Dictionary<string, object> { ["records"] = 2 },
            };
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["model_version"] = "test-1",
                ["artifacts"] = artifacts,
            });
        }
    }
}
