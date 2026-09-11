using System.Buffers.Binary;

namespace SmartInput.Infrastructure.Security;

/// <summary>
/// Structural audit data only. IDs remain opaque; this type has no concept of
/// words, language, replacement, or acceptance.
/// </summary>
public sealed class DawgReachabilityAudit
{
    public DawgReachabilityAudit(int reachableStateCount, IReadOnlyCollection<int> outputIds)
    {
        ReachableStateCount = reachableStateCount;
        OutputIds = outputIds.Order().ToArray();
    }

    public int ReachableStateCount { get; }
    public IReadOnlyList<int> OutputIds { get; }
}

/// <summary>
/// Bounds-checked reader for the recovered DAWG-like pattern automata. It
/// implements only the transition and output-link formulas confirmed by
/// reverse engineering; output IDs are opaque until their rule tables are
/// decoded.
/// </summary>
public sealed class DawgBlockReader
{
    private const uint LabelMask = 0x800000FFu;
    private readonly uint[] _records;

    private DawgBlockReader(uint[] records)
    {
        _records = records;
    }

    public int RecordCount => _records.Length;

    public static DawgBlockReader Parse(ReadOnlySpan<byte> block)
    {
        if (block.Length < sizeof(uint) * 2 || block.Length % sizeof(uint) != 0)
        {
            throw new FormatException("DAWG block must contain a count and at least one record.");
        }

        var declaredCount = BinaryPrimitives.ReadUInt32LittleEndian(block);
        var recordCount = block.Length / sizeof(uint) - 1;
        if (declaredCount != recordCount || declaredCount == 0)
        {
            throw new FormatException("DAWG record count does not match the block length.");
        }

        var records = new uint[recordCount];
        for (var index = 0; index < recordCount; index++)
        {
            records[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                block.Slice((index + 1) * sizeof(uint), sizeof(uint)));
        }

        return new DawgBlockReader(records);
    }

    public bool TryTransition(int state, byte label, out int nextState)
    {
        nextState = -1;
        if ((uint)state >= (uint)_records.Length)
        {
            return false;
        }

        var record = _records[state];
        var baseValue = ((ulong)(record >> 10)) << ((record & 0x200u) != 0 ? 8 : 0);
        var candidate = (long)state ^ label ^ (long)baseValue;
        if (candidate < 0 || candidate >= _records.Length)
        {
            return false;
        }

        var candidateIndex = (int)candidate;
        if ((_records[candidateIndex] & LabelMask) != label)
        {
            return false;
        }

        nextState = candidateIndex;
        return true;
    }

    /// <summary>
    /// Returns the opaque rule ID emitted by a state with the confirmed
    /// 0x100 output-link flag. This is deliberately not a word-terminal test.
    /// </summary>
    public bool TryGetOutputId(int state, out int outputId)
    {
        outputId = -1;
        if ((uint)state >= (uint)_records.Length
            || (_records[state] & 0x80000000u) != 0
            || (_records[state] & 0x100u) == 0)
        {
            return false;
        }

        var record = _records[state];
        var baseValue = ((ulong)(record >> 10)) << ((record & 0x200u) != 0 ? 8 : 0);
        var outputIndex = (long)state ^ (long)baseValue;
        if (outputIndex < 0 || outputIndex >= _records.Length)
        {
            return false;
        }

        var rawOutput = _records[(int)outputIndex] & 0x7FFFFFFFu;
        if (rawOutput > int.MaxValue)
        {
            return false;
        }

        outputId = (int)rawOutput;
        return true;
    }

    public IReadOnlyList<byte> GetOutgoingLabels(int state)
    {
        if ((uint)state >= (uint)_records.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var labels = new List<byte>();
        for (var label = 0; label <= byte.MaxValue; label++)
        {
            if (TryTransition(state, (byte)label, out _))
            {
                labels.Add((byte)label);
            }
        }

        return labels;
    }

    /// <summary>
    /// Follows a byte sequence and returns the reached state. A reached state
    /// is not treated as an accepted word until a separately verified
    /// terminal-state rule is supplied by the integration layer.
    /// </summary>
    public bool TryFollow(ReadOnlySpan<byte> labels, out int state)
    {
        state = 0;
        foreach (var label in labels)
        {
            if (!TryTransition(state, label, out state))
            {
                state = -1;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Enumerates all states structurally reachable from state zero and their
    /// opaque output-link IDs. This is intended for an explicit offline audit,
    /// not for a keyboard-hook path.
    /// </summary>
    public DawgReachabilityAudit AuditReachableOutputs()
    {
        var visited = new bool[_records.Length];
        var pending = new Queue<int>();
        var outputIds = new HashSet<int>();
        visited[0] = true;
        pending.Enqueue(0);

        while (pending.Count > 0)
        {
            var state = pending.Dequeue();
            if (TryGetOutputId(state, out var outputId))
            {
                outputIds.Add(outputId);
            }

            for (var label = 0; label <= byte.MaxValue; label++)
            {
                if (TryTransition(state, (byte)label, out var nextState) && !visited[nextState])
                {
                    visited[nextState] = true;
                    pending.Enqueue(nextState);
                }
            }
        }

        return new DawgReachabilityAudit(visited.Count(static state => state), outputIds);
    }
}
