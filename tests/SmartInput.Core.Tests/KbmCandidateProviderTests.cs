using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartInput.Core.Integration;
using SmartInput.Infrastructure.RecoveredRules;

namespace SmartInput.Core.Tests;

public sealed class KbmCandidateProviderTests
{
    [Fact]
    public void PreparedInput_UsesOnlyTheExplicitMarkerRoute()
    {
        Assert.Equal(new byte[] { 0x78 },
            KbmPreparedInput.FromToken("x", KbmMarkerRoute.Raw).Bytes.ToArray());
        Assert.Equal(new byte[] { 0x02, 0x78 },
            KbmPreparedInput.FromToken("x", KbmMarkerRoute.Prefix02).Bytes.ToArray());
        Assert.Equal(new byte[] { 0x78, 0x03 },
            KbmPreparedInput.FromToken("x", KbmMarkerRoute.Suffix03).Bytes.ToArray());
        Assert.Equal(new byte[] { 0x02, 0x78, 0x03 },
            KbmPreparedInput.FromToken("x", KbmMarkerRoute.Wrapped0203).Bytes.ToArray());
    }

    [Fact]
    public void Provider_ResolvesExactOutputIdAndPreservesOpaqueMetadata()
    {
        using var fixture = new KbmFixture();
        var provider = KbmCandidateProvider.Load(
            fixture.ModelDirectory,
            fixture.DawgSha256,
            requireCompletePool: false);

        var input = KbmPreparedInput.FromRawBytes([1], KbmMarkerRoute.Raw);
        Assert.True(provider.TryResolve(input, out var candidate));
        Assert.NotNull(candidate);
        Assert.Equal(7, candidate.OutputId);
        Assert.Equal("hello", candidate.Text);
        Assert.Equal("010001", candidate.MetadataHex);
        Assert.Equal(KbmMarkerRoute.Raw, candidate.Route);
        Assert.Equal(fixture.DawgSha256, candidate.ModelSha256);
    }

    [Fact]
    public void Provider_DoesNotGuessAnotherRouteOrExposeApply()
    {
        using var fixture = new KbmFixture();
        var provider = KbmCandidateProvider.Load(fixture.ModelDirectory, requireCompletePool: false);

        // The synthetic DAWG contains only the raw byte [1]. A prefix marker
        // must not be stripped or retried as a fuzzy fallback.
        var prefixed = KbmPreparedInput.FromRawBytes([1], KbmMarkerRoute.Prefix02);
        Assert.False(provider.TryResolve(prefixed, out var candidate));
        Assert.Null(candidate);

        var disabled = new NullKbmCandidateProvider();
        Assert.False(disabled.TryResolve(
            KbmPreparedInput.FromRawBytes([1], KbmMarkerRoute.Raw), out candidate));
        Assert.Null(candidate);
    }

    [Fact]
    public void Provider_RejectsTamperedDawgBeforeLookup()
    {
        using var fixture = new KbmFixture();
        File.WriteAllBytes(Path.Combine(fixture.ModelDirectory, "kbm-lexicon.dawg"), [1, 2, 3]);

        Assert.Throws<CryptographicException>(() =>
            KbmCandidateProvider.Load(fixture.ModelDirectory, fixture.DawgSha256, requireCompletePool: false));
    }

    [Fact]
    public void Provider_RejectsDuplicatePoolIds()
    {
        using var fixture = new KbmFixture(duplicatePoolId: true);

        Assert.Throws<FormatException>(() =>
            KbmCandidateProvider.Load(fixture.ModelDirectory, requireCompletePool: false));
    }

    [Fact]
    public void RealHandoffModel_ResolvesEveryCanonicalWitness_WhenConfigured()
    {
        var modelDirectory = Environment.GetEnvironmentVariable("SMARTINPUT_KBM_MODEL_DIR");
        if (string.IsNullOrWhiteSpace(modelDirectory)
            || !File.Exists(Path.Combine(modelDirectory, "kbm-lexicon.dawg"))
            || !File.Exists(Path.Combine(modelDirectory, "kbm-indexed-text-pool.jsonl"))
            || !File.Exists(Path.Combine(modelDirectory, "kbm-terminal-canonical-index.jsonl")))
        {
            return;
        }

        var provider = KbmCandidateProvider.Load(
            modelDirectory,
            expectedDawgSha256: "1ac698262db2c425db37a4d078faf72753c55a7070bbbb367356f1c4d256702a");
        Assert.Equal(373_504, provider.DawgRecordCount);
        Assert.Equal(70_007, provider.PoolEntryCount);

        foreach (var line in File.ReadLines(Path.Combine(modelDirectory, "kbm-terminal-canonical-index.jsonl")))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var outputId = root.GetProperty("output_id").GetInt32();
            var target = root.GetProperty("target_text").GetString();
            var prepared = Convert.FromBase64String(root.GetProperty("prepared_key_base64").GetString()!);
            var route = Enum.Parse<KbmMarkerRoute>(
                root.GetProperty("marker_shape").GetString() switch
                {
                    "prefix_02" => nameof(KbmMarkerRoute.Prefix02),
                    "suffix_03" => nameof(KbmMarkerRoute.Suffix03),
                    "wrapped_02_03" => nameof(KbmMarkerRoute.Wrapped0203),
                    "raw" => nameof(KbmMarkerRoute.Raw),
                    _ => throw new FormatException("Unknown KBM marker shape."),
                });

            Assert.True(provider.TryResolve(
                KbmPreparedInput.FromPreparedBytes(prepared, route), out var candidate));
            Assert.NotNull(candidate);
            Assert.Equal(outputId, candidate.OutputId);
            Assert.Equal(target, candidate.Text);
        }
    }

    private sealed class KbmFixture : IDisposable
    {
        public KbmFixture(bool duplicatePoolId = false)
        {
            Root = Path.Combine(Path.GetTempPath(), "smartinput-kbm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);

            var dawg = BuildOutputBlock(7);
            var dawgPath = Path.Combine(Root, "kbm-lexicon.dawg");
            File.WriteAllBytes(dawgPath, dawg);
            DawgSha256 = Convert.ToHexString(SHA256.HashData(dawg));

            var rows = new[]
            {
                JsonSerializer.Serialize(new { id = 7, text = "hello", metadata_hex = "010001" }),
            };
            if (duplicatePoolId)
            {
                rows = rows.Append(
                    JsonSerializer.Serialize(new { id = 7, text = "other", metadata_hex = "000000" }))
                    .ToArray();
            }

            File.WriteAllText(
                Path.Combine(Root, "kbm-indexed-text-pool.jsonl"),
                string.Join(Environment.NewLine, rows),
                Encoding.UTF8);
        }

        public string Root { get; }
        public string ModelDirectory => Root;
        public string DawgSha256 { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static byte[] BuildOutputBlock(uint outputId)
        {
            var records = new uint[4];
            records[0] = 1;
            records[1] = 1u | 0x100u | (2u << 10);
            records[3] = outputId;
            var block = new byte[(records.Length + 1) * sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(block, (uint)records.Length);
            for (var index = 0; index < records.Length; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    block.AsSpan((index + 1) * sizeof(uint)), records[index]);
            }

            return block;
        }
    }
}
