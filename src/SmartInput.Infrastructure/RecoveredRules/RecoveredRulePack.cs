using System.Security.Cryptography;
using System.Text.Json;
using SmartInput.Infrastructure.Security;

namespace SmartInput.Infrastructure.RecoveredRules;

/// <summary>Identifies a recovered pattern automaton, not a natural language.</summary>
public enum RecoveredAutomatonKind
{
    A,
    B,
}

public enum RecoveredRuleMatchStatus
{
    EmptyInput,
    CompletePath,
    IncompletePath,
    SubstringScanCompleted,
}

public enum RecoveredMatchDirection
{
    Forward,
    Reverse,
}

/// <summary>
/// An output emitted by a recovered automaton while processing already
/// prepared bytes. It intentionally contains no token text or replacement.
/// </summary>
public sealed record RecoveredRuleMatch(
    RecoveredAutomatonKind Source,
    int OutputId,
    int ByteOffset);

public sealed class RecoveredRuleAuditResult
{
    public RecoveredRuleAuditResult(
        RecoveredRuleMatchStatus status,
        int processedByteCount,
        IReadOnlyList<RecoveredRuleMatch> matches)
    {
        Status = status;
        ProcessedByteCount = processedByteCount;
        Matches = matches.ToArray();
    }

    public RecoveredRuleMatchStatus Status { get; }
    public int ProcessedByteCount { get; }
    public IReadOnlyList<RecoveredRuleMatch> Matches { get; }
}

/// <summary>
/// Offline proof that every reachable opaque ID has a raw record and every
/// raw record is reachable. It makes no semantic claim about those records.
/// </summary>
public sealed class RecoveredRuleBindingAudit
{
    internal RecoveredRuleBindingAudit(
        DawgReachabilityAudit automatonA,
        DawgReachabilityAudit automatonB,
        IReadOnlyCollection<int> missingARuleIds,
        IReadOnlyCollection<int> orphanARuleIds,
        IReadOnlyCollection<int> missingBRuleIds,
        IReadOnlyCollection<int> orphanBRuleIds)
    {
        AutomatonA = automatonA;
        AutomatonB = automatonB;
        MissingARuleIds = missingARuleIds.Order().ToArray();
        OrphanARuleIds = orphanARuleIds.Order().ToArray();
        MissingBRuleIds = missingBRuleIds.Order().ToArray();
        OrphanBRuleIds = orphanBRuleIds.Order().ToArray();
    }

    public DawgReachabilityAudit AutomatonA { get; }
    public DawgReachabilityAudit AutomatonB { get; }
    public IReadOnlyList<int> MissingARuleIds { get; }
    public IReadOnlyList<int> OrphanARuleIds { get; }
    public IReadOnlyList<int> MissingBRuleIds { get; }
    public IReadOnlyList<int> OrphanBRuleIds { get; }
    public bool IsExact => MissingARuleIds.Count == 0 && OrphanARuleIds.Count == 0
        && MissingBRuleIds.Count == 0 && OrphanBRuleIds.Count == 0;
}

/// <summary>
/// An opaque recovered record. Its bytes are available only to an explicit
/// research caller and are never interpreted as text, language or a replacement.
/// </summary>
public sealed class OpaqueRecoveredRule
{
    private readonly byte[] _rawRecord;

    internal OpaqueRecoveredRule(RecoveredAutomatonKind source, int outputId, ReadOnlySpan<byte> rawRecord)
    {
        Source = source;
        OutputId = outputId;
        _rawRecord = rawRecord.ToArray();
        RawRecordSha256 = Convert.ToHexString(SHA256.HashData(_rawRecord));
    }

    public RecoveredAutomatonKind Source { get; }
    public int OutputId { get; }
    public string RawRecordSha256 { get; }

    public byte[] CopyRawRecord() => _rawRecord.ToArray();
}

/// <summary>
/// Immutable, audit-only reader for the recovered Caramba rule pack.
/// It cannot produce a replacement or invoke any input API.
/// </summary>
public sealed class RecoveredRulePack
{
    private const int MaximumAutomatonBytes = 4 * 1024 * 1024;
    private const int MaximumAuxiliaryBytes = 128 * 1024;
    private const int MaximumRuleRecordBytes = 4 * 1024;

    private readonly DawgBlockReader _automatonA;
    private readonly DawgBlockReader _automatonB;
    private readonly Dictionary<int, byte[]> _aRules;
    private readonly byte[][] _bRules;
    private readonly byte[] _auxiliary;
    private readonly byte[][] _group2Field4;
    private readonly byte[][] _group3Field3;
    private readonly byte[][] _group5Field3;

    private RecoveredRulePack(
        DawgBlockReader automatonA,
        DawgBlockReader automatonB,
        Dictionary<int, byte[]> aRules,
        byte[][] bRules,
        byte[] auxiliary,
        byte[][] group2Field4,
        byte[][] group3Field3,
        byte[][] group5Field3,
        string modelVersion,
        string automatonASha256,
        string automatonBSha256)
    {
        _automatonA = automatonA;
        _automatonB = automatonB;
        _aRules = aRules;
        _bRules = bRules;
        _auxiliary = auxiliary;
        _group2Field4 = group2Field4;
        _group3Field3 = group3Field3;
        _group5Field3 = group5Field3;
        ModelVersion = modelVersion;
        AutomatonASha256 = automatonASha256;
        AutomatonBSha256 = automatonBSha256;
    }

    public int AutomatonARecordCount => _automatonA.RecordCount;
    public int AutomatonBRecordCount => _automatonB.RecordCount;
    public int AutomatonARuleCount => _aRules.Count;
    public int AutomatonBRuleCount => _bRules.Length;
    public int OpaqueLinkedRuleCount => _group5Field3.Length;
    /// <summary>Manifest metadata for audit screens; never used for a correction decision.</summary>
    public string ModelVersion { get; }
    public string AutomatonASha256 { get; }
    public string AutomatonBSha256 { get; }

    public static RecoveredRulePack Load(string integrationDirectory)
    {
        if (string.IsNullOrWhiteSpace(integrationDirectory))
        {
            throw new ArgumentException("An integration directory is required.", nameof(integrationDirectory));
        }

        var root = Path.GetFullPath(integrationDirectory);
        var manifest = ReadManifest(Path.Combine(root, "model-blocks-manifest.json"));
        var hashes = ReadPackageHashes(Directory.GetParent(root)?.FullName
            ?? throw new FormatException("Recovered package root could not be resolved."));

        var automatonABytes = ReadVerifiedFile(
            Path.Combine(root, "dawg", "lexicon-a.dawg"),
            manifest.LexiconASha256,
            hashes.LexiconASha256,
            MaximumAutomatonBytes);
        var automatonBBytes = ReadVerifiedFile(
            Path.Combine(root, "dawg", "lexicon-b.dawg"),
            manifest.LexiconBSha256,
            hashes.LexiconBSha256,
            MaximumAutomatonBytes);

        var aRules = ReadARules(
            Path.Combine(root, "rules", "group-2-field-3.jsonl"),
            manifest.Group2Field3Count);
        var bRules = ReadRawJsonLines(
            Path.Combine(root, "rules", "group-5-field-2.jsonl"),
            manifest.Group5Field2Count);
        var group2Field4 = ReadRawJsonLines(
            Path.Combine(root, "rules", "group-2-field-4.jsonl"),
            manifest.Group2Field4Count);
        var group3Field3 = ReadRawJsonLines(
            Path.Combine(root, "rules", "group-3-field-3.jsonl"),
            manifest.Group3Field3Count);
        var group5Field3 = ReadRawJsonLines(
            Path.Combine(root, "rules", "group-5-field-3.jsonl"),
            manifest.Group5Field3Count);
        var auxiliary = ReadBoundedFile(
            Path.Combine(root, "auxiliary", "group-3-field-2.bin"),
            MaximumAuxiliaryBytes);

        return new RecoveredRulePack(
            DawgBlockReader.Parse(automatonABytes),
            DawgBlockReader.Parse(automatonBBytes),
            aRules,
            bRules,
            auxiliary,
            group2Field4,
            group3Field3,
            group5Field3,
            manifest.ModelVersion,
            manifest.LexiconASha256,
            manifest.LexiconBSha256);
    }

    /// <summary>
    /// Runs a direct path over already prepared bytes. The original preprocessor
    /// is not recovered, therefore a CompletePath is audit data only.
    /// </summary>
    public RecoveredRuleAuditResult Match(
        RecoveredAutomatonKind source,
        ReadOnlySpan<byte> preparedBytes)
    {
        if (preparedBytes.IsEmpty)
        {
            return new RecoveredRuleAuditResult(RecoveredRuleMatchStatus.EmptyInput, 0, []);
        }

        var automaton = source == RecoveredAutomatonKind.A ? _automatonA : _automatonB;
        var matches = new List<RecoveredRuleMatch>();
        var emitted = new HashSet<int>();
        var state = 0;
        for (var index = 0; index < preparedBytes.Length; index++)
        {
            if (!automaton.TryTransition(state, preparedBytes[index], out state))
            {
                return new RecoveredRuleAuditResult(
                    RecoveredRuleMatchStatus.IncompletePath,
                    index,
                    matches);
            }

            if (automaton.TryGetOutputId(state, out var outputId) && emitted.Add(outputId))
            {
                matches.Add(new RecoveredRuleMatch(source, outputId, index));
            }
        }

        return new RecoveredRuleAuditResult(
            RecoveredRuleMatchStatus.CompletePath,
            preparedBytes.Length,
            matches);
    }

    /// <summary>
    /// Reproduces the confirmed native forward/reverse substring loops. The
    /// caller supplies already prepared bytes; no normalization is inferred.
    /// Every byte offset starts a fresh state-0 walk and every output-link hit
    /// is returned, including repeated IDs. This method is audit-only.
    /// </summary>
    public RecoveredRuleAuditResult MatchSubstrings(
        RecoveredAutomatonKind source,
        RecoveredMatchDirection direction,
        ReadOnlySpan<byte> preparedBytes)
    {
        if (preparedBytes.IsEmpty)
        {
            return new RecoveredRuleAuditResult(RecoveredRuleMatchStatus.EmptyInput, 0, []);
        }

        var automaton = source == RecoveredAutomatonKind.A ? _automatonA : _automatonB;
        var matches = new List<RecoveredRuleMatch>();
        if (direction == RecoveredMatchDirection.Forward)
        {
            for (var start = 0; start < preparedBytes.Length; start++)
            {
                var state = 0;
                for (var index = start; index < preparedBytes.Length; index++)
                {
                    if (!automaton.TryTransition(state, preparedBytes[index], out state))
                    {
                        break;
                    }

                    if (automaton.TryGetOutputId(state, out var outputId))
                    {
                        matches.Add(new RecoveredRuleMatch(source, outputId, index));
                    }
                }
            }
        }
        else
        {
            for (var end = 1; end <= preparedBytes.Length; end++)
            {
                var state = 0;
                for (var index = end - 1; index >= 0; index--)
                {
                    if (!automaton.TryTransition(state, preparedBytes[index], out state))
                    {
                        break;
                    }

                    if (automaton.TryGetOutputId(state, out var outputId))
                    {
                        matches.Add(new RecoveredRuleMatch(source, outputId, index));
                    }
                }
            }
        }

        return new RecoveredRuleAuditResult(
            RecoveredRuleMatchStatus.SubstringScanCompleted,
            preparedBytes.Length,
            matches);
    }

    public RecoveredRuleAuditResult MatchSubstrings(
        RecoveredAutomatonKind source,
        RecoveredMatchDirection direction,
        byte[] preparedBytes)
    {
        ArgumentNullException.ThrowIfNull(preparedBytes);
        return MatchSubstrings(source, direction, preparedBytes.AsSpan());
    }

    public bool TryResolveRule(
        RecoveredAutomatonKind source,
        int outputId,
        out OpaqueRecoveredRule? rule)
    {
        rule = null;
        if (outputId < 0)
        {
            return false;
        }

        if (source == RecoveredAutomatonKind.A)
        {
            if (!_aRules.TryGetValue(outputId, out var rawA))
            {
                return false;
            }

            rule = new OpaqueRecoveredRule(source, outputId, rawA);
            return true;
        }

        if ((uint)outputId >= (uint)_bRules.Length)
        {
            return false;
        }

        rule = new OpaqueRecoveredRule(source, outputId, _bRules[outputId]);
        return true;
    }

    /// <summary>
    /// Resolves the confirmed structural B-table link. Both returned records
    /// remain opaque; the link index is not interpreted as a replacement or
    /// language value.
    /// </summary>
    public bool TryResolveBLinkedRecord(
        int outputId,
        out int linkedIndex,
        out OpaqueRecoveredRule? outputRecord,
        out OpaqueRecoveredRule? linkedRecord)
    {
        linkedIndex = -1;
        outputRecord = null;
        linkedRecord = null;
        if ((uint)outputId >= (uint)_bRules.Length)
        {
            return false;
        }

        var index = ReadRequiredVarintField(_bRules[outputId], fieldNumber: 3);
        if (index == 0 || index >= (ulong)_group5Field3.Length)
        {
            return false;
        }

        linkedIndex = (int)index;
        outputRecord = new OpaqueRecoveredRule(RecoveredAutomatonKind.B, outputId, _bRules[outputId]);
        linkedRecord = new OpaqueRecoveredRule(RecoveredAutomatonKind.B, linkedIndex, _group5Field3[linkedIndex]);
        return true;
    }

    public byte[] CopyAuxiliaryBlock() => _auxiliary.ToArray();
    public IReadOnlyList<byte[]> CopyOpaqueLinkedRules() => CopyRecords(_group5Field3);
    public IReadOnlyList<byte[]> CopyGroup2Field4Records() => CopyRecords(_group2Field4);
    public IReadOnlyList<byte[]> CopyGroup3Field3Records() => CopyRecords(_group3Field3);

    /// <summary>
    /// Explicit, potentially expensive offline verification of output ID ↔ raw
    /// record coverage. It is intentionally not called by <see cref="Load"/>
    /// and is never eligible for a typing path.
    /// </summary>
    public RecoveredRuleBindingAudit AuditOpaqueBindings()
    {
        var a = _automatonA.AuditReachableOutputs();
        var b = _automatonB.AuditReachableOutputs();
        var aOutputs = a.OutputIds.ToHashSet();
        var bOutputs = b.OutputIds.ToHashSet();
        var aRules = _aRules.Keys.ToHashSet();
        var bRules = Enumerable.Range(0, _bRules.Length).ToHashSet();
        return new RecoveredRuleBindingAudit(
            a,
            b,
            aOutputs.Except(aRules).ToArray(),
            aRules.Except(aOutputs).ToArray(),
            bOutputs.Except(bRules).ToArray(),
            bRules.Except(bOutputs).ToArray());
    }

    private static IReadOnlyList<byte[]> CopyRecords(IEnumerable<byte[]> records)
        => records.Select(static record => record.ToArray()).ToArray();

    private static Dictionary<int, byte[]> ReadARules(string path, int expectedCount)
    {
        var records = ReadRawJsonLines(path, expectedCount);
        var byId = new Dictionary<int, byte[]>(records.Length);
        foreach (var record in records)
        {
            var id = ReadRequiredVarintField(record, fieldNumber: 1);
            if (id > int.MaxValue || !byId.TryAdd((int)id, record))
            {
                throw new FormatException("Recovered A rule IDs are invalid or duplicated.");
            }
        }

        return byId;
    }

    private static byte[][] ReadRawJsonLines(string path, int expectedCount)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Recovered rule table is missing.", path);
        }

        var records = new List<byte[]>(expectedCount);
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("raw_base64", out var rawElement)
                || rawElement.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("Recovered rule JSONL record has no raw bytes.");
            }

            var rawText = rawElement.GetString();
            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(rawText ?? string.Empty);
            }
            catch (FormatException exception)
            {
                throw new FormatException("Recovered rule JSONL record is not valid base64.", exception);
            }

            if (raw.Length > MaximumRuleRecordBytes)
            {
                throw new FormatException("Recovered rule record exceeds its size limit.");
            }

            records.Add(raw);
        }

        if (records.Count != expectedCount)
        {
            throw new FormatException("Recovered rule table count does not match the manifest.");
        }

        return records.ToArray();
    }

    private static byte[] ReadVerifiedFile(
        string path,
        string manifestHash,
        string packageHash,
        int maximumLength)
    {
        if (!string.Equals(manifestHash, packageHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("Recovered automaton manifests disagree.");
        }

        var content = ReadBoundedFile(path, maximumLength);
        var actualHash = Convert.ToHexString(SHA256.HashData(content));
        if (!HashEquals(actualHash, manifestHash))
        {
            throw new CryptographicException("Recovered automaton integrity check failed.");
        }

        return content;
    }

    private static byte[] ReadBoundedFile(string path, int maximumLength)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Recovered package artifact is missing.", path);
        }

        if (info.Length < 0 || info.Length > maximumLength)
        {
            throw new FormatException("Recovered package artifact exceeds its size limit.");
        }

        return File.ReadAllBytes(path);
    }

    private static bool HashEquals(string actualHash, string expectedHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static ulong ReadRequiredVarintField(ReadOnlySpan<byte> message, int fieldNumber)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var number = checked((int)(key >> 3));
            var wireType = (int)(key & 7);
            if (number <= 0)
            {
                throw new FormatException("Recovered rule field number is invalid.");
            }

            if (wireType == 0)
            {
                var value = ReadVarint(message, ref offset);
                if (number == fieldNumber)
                {
                    return value;
                }

                continue;
            }

            SkipField(message, ref offset, wireType);
        }

        throw new FormatException("Recovered A rule has no linked output ID.");
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> value, ref int offset)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= value.Length)
            {
                throw new FormatException("Recovered rule is truncated.");
            }

            var current = value[offset++];
            if (shift == 63 && current > 1)
            {
                throw new FormatException("Recovered rule varint overflows 64 bits.");
            }

            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return result;
            }
        }

        throw new FormatException("Recovered rule varint is too long.");
    }

    private static void SkipField(ReadOnlySpan<byte> value, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint(value, ref offset);
                return;
            case 1:
                EnsureRemaining(value, offset, 8);
                offset += 8;
                return;
            case 2:
                var length = checked((int)ReadVarint(value, ref offset));
                EnsureRemaining(value, offset, length);
                offset += length;
                return;
            case 5:
                EnsureRemaining(value, offset, 4);
                offset += 4;
                return;
            default:
                throw new FormatException("Recovered rule uses an unsupported wire type.");
        }
    }

    private static void EnsureRemaining(ReadOnlySpan<byte> value, int offset, int count)
    {
        if (offset < 0 || count < 0 || count > value.Length - offset)
        {
            throw new FormatException("Recovered rule field is truncated.");
        }
    }

    private static RecoveredRulePackManifest ReadManifest(string path)
    {
        using var document = JsonDocument.Parse(ReadBoundedFile(path, 512 * 1024));
        var artifacts = document.RootElement.GetProperty("artifacts");
        return new RecoveredRulePackManifest(
            ReadRequiredString(document.RootElement, "model_version"),
            ReadArtifactHash(artifacts, "dawg/lexicon-a.dawg"),
            ReadArtifactHash(artifacts, "dawg/lexicon-b.dawg"),
            ReadArtifactCount(artifacts, "rules/group-2-field-3.jsonl"),
            ReadArtifactCount(artifacts, "rules/group-2-field-4.jsonl"),
            ReadArtifactCount(artifacts, "rules/group-3-field-3.jsonl"),
            ReadArtifactCount(artifacts, "rules/group-5-field-2.jsonl"),
            ReadArtifactCount(artifacts, "rules/group-5-field-3.jsonl"));
    }

    private static RecoveredRulePackHashes ReadPackageHashes(string packageRoot)
    {
        var path = Path.Combine(packageRoot, "hashes.sha256");
        string? a = null;
        string? b = null;
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            if (parts[^1].EndsWith("integration/dawg/lexicon-a.dawg", StringComparison.OrdinalIgnoreCase))
            {
                a = parts[0];
            }
            else if (parts[^1].EndsWith("integration/dawg/lexicon-b.dawg", StringComparison.OrdinalIgnoreCase))
            {
                b = parts[0];
            }
        }

        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            throw new FormatException("Recovered package SHA-256 entries are missing.");
        }

        return new RecoveredRulePackHashes(a, b);
    }

    private static string ReadArtifactHash(JsonElement artifacts, string name)
        => artifacts.GetProperty(name).GetProperty("sha256").GetString()
            ?? throw new FormatException("Recovered package artifact hash is missing.");

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new FormatException("Recovered package manifest metadata is invalid.");
        }

        return value;
    }

    private static int ReadArtifactCount(JsonElement artifacts, string name)
        => artifacts.GetProperty(name).GetProperty("records").GetInt32();

    private sealed record RecoveredRulePackManifest(
        string ModelVersion,
        string LexiconASha256,
        string LexiconBSha256,
        int Group2Field3Count,
        int Group2Field4Count,
        int Group3Field3Count,
        int Group5Field2Count,
        int Group5Field3Count);

    private sealed record RecoveredRulePackHashes(string LexiconASha256, string LexiconBSha256);
}
