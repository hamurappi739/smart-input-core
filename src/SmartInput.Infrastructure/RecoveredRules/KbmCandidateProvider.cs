using System.Security.Cryptography;
using System.Text.Json;
using SmartInput.Core.Integration;
using SmartInput.Infrastructure.Security;

namespace SmartInput.Infrastructure.RecoveredRules;

/// <summary>
/// Exact KBM DAWG → indexed text-pool reader. This class is deliberately an
/// observation provider: it does not choose a route, score a candidate, apply
/// text, call SendInput or create Undo state.
/// </summary>
public sealed class KbmCandidateProvider : IKbmCandidateProvider
{
    public const string KnownDawgSha256 =
        "1ac698262db2c425db37a4d078faf72753c55a7070bbbb367356f1c4d256702a";
    private const int MaximumDawgBytes = 8 * 1024 * 1024;
    private const int MaximumPoolFileBytes = 64 * 1024 * 1024;
    private const int MaximumPoolEntries = 100_000;
    private const int ExpectedFullPoolCount = 70_007;
    private readonly DawgBlockReader _dawg;
    private readonly IReadOnlyDictionary<int, KbmPoolEntry> _pool;

    private KbmCandidateProvider(
        DawgBlockReader dawg,
        IReadOnlyDictionary<int, KbmPoolEntry> pool,
        string modelSha256)
    {
        _dawg = dawg;
        _pool = pool;
        ModelSha256 = modelSha256;
    }

    public int DawgRecordCount => _dawg.RecordCount;
    public int PoolEntryCount => _pool.Count;
    public string ModelSha256 { get; }

    /// <summary>
    /// Loads a model directory containing kbm-lexicon.dawg and
    /// kbm-indexed-text-pool.jsonl. The known hash is optional for synthetic
    /// tests, but production callers should always provide it.
    /// </summary>
    public static KbmCandidateProvider Load(
        string modelDirectory,
        string? expectedDawgSha256 = null,
        bool requireCompletePool = true)
    {
        if (string.IsNullOrWhiteSpace(modelDirectory))
        {
            throw new ArgumentException("A model directory is required.", nameof(modelDirectory));
        }

        var root = Path.GetFullPath(modelDirectory);
        var dawgPath = Path.Combine(root, "kbm-lexicon.dawg");
        var poolPath = Path.Combine(root, "kbm-indexed-text-pool.jsonl");
        var dawgBytes = ReadBounded(dawgPath, MaximumDawgBytes);
        var modelSha256 = Convert.ToHexString(SHA256.HashData(dawgBytes));
        if (!string.IsNullOrWhiteSpace(expectedDawgSha256)
            && !FixedHashEquals(modelSha256, expectedDawgSha256))
        {
            throw new CryptographicException("KBM DAWG integrity check failed.");
        }

        var pool = ReadPool(poolPath, requireCompletePool);
        return new KbmCandidateProvider(DawgBlockReader.Parse(dawgBytes), pool, modelSha256);
    }

    public bool TryResolve(KbmPreparedInput input, out KbmCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(input);
        candidate = null;
        if (input.Bytes.Length == 0 || input.Bytes.Length > 4096)
        {
            return false;
        }

        if (!_dawg.TryFollow(input.Bytes.Span, out var state)
            || !_dawg.TryGetOutputId(state, out var outputId)
            || !_pool.TryGetValue(outputId, out var entry))
        {
            return false;
        }

        candidate = new KbmCandidate(
            outputId,
            entry.Text,
            entry.MetadataHex,
            input.Route,
            ModelSha256);
        return true;
    }

    private static IReadOnlyDictionary<int, KbmPoolEntry> ReadPool(
        string path,
        bool requireCompletePool)
    {
        var bytes = ReadBounded(path, MaximumPoolFileBytes);
        var entries = new Dictionary<int, KbmPoolEntry>();
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream);
        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (entries.Count >= MaximumPoolEntries)
            {
                throw new FormatException("KBM text pool exceeds its entry limit.");
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var id = root.GetProperty("id").GetInt32();
                var text = root.GetProperty("text").GetString();
                var metadata = root.GetProperty("metadata_hex").GetString();
                if (id < 0 || id >= MaximumPoolEntries || string.IsNullOrEmpty(text)
                    || string.IsNullOrWhiteSpace(metadata)
                    || metadata.Length > 64
                    || metadata.Length % 2 != 0
                    || !IsHex(metadata))
                {
                    throw new FormatException("KBM pool entry has invalid fields.");
                }

                if (!entries.TryAdd(id, new KbmPoolEntry(text, metadata)))
                {
                    throw new FormatException("KBM text pool contains a duplicate ID.");
                }
            }
            catch (JsonException exception)
            {
                throw new FormatException($"KBM text pool JSON is invalid at line {lineNumber}.", exception);
            }
        }

        if (requireCompletePool
            && (entries.Count != ExpectedFullPoolCount
                || entries.Keys.Min() != 0
                || entries.Keys.Max() != ExpectedFullPoolCount - 1))
        {
            throw new FormatException("KBM text pool is not the complete 0..70006 pool.");
        }

        return entries;
    }

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("KBM model artifact is missing.", path);
        }

        if (info.Length > maximumBytes)
        {
            throw new FormatException("KBM model artifact exceeds its size limit.");
        }

        return File.ReadAllBytes(path);
    }

    private static bool FixedHashEquals(string actual, string expected)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsHex(string value)
        => value.All(static character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');

    private sealed record KbmPoolEntry(string Text, string MetadataHex);
}
