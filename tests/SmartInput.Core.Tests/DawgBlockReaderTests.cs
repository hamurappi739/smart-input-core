using System.Buffers.Binary;
using SmartInput.Infrastructure.Security;

namespace SmartInput.Core.Tests;

public sealed class DawgBlockReaderTests
{
    [Fact]
    public void ParseAndTransition_UsesConfirmedFormula()
    {
        var block = BuildBlock(
            0u,
            1u,
            2u);

        var reader = DawgBlockReader.Parse(block);

        Assert.Equal(3, reader.RecordCount);
        Assert.True(reader.TryTransition(0, 1, out var state));
        Assert.Equal(1, state);
        Assert.True(reader.TryTransition(0, 2, out state));
        Assert.Equal(2, state);
        Assert.True(reader.TryFollow(new byte[] { 1 }, out state));
        Assert.Equal(1, state);
    }

    [Fact]
    public void Transition_AppliesPackedBaseAndRejectsOutOfBounds()
    {
        var records = new uint[7];
        records[0] = 4u << 10;
        records[5] = 1u;
        var reader = DawgBlockReader.Parse(BuildBlock(records));

        Assert.True(reader.TryTransition(0, 1, out var state));
        Assert.Equal(5, state);
        Assert.False(reader.TryTransition(0, 2, out _));
        Assert.False(reader.TryTransition(-1, 1, out _));
        Assert.False(reader.TryTransition(99, 1, out _));
    }

    [Fact]
    public void Parse_RejectsCountMismatchAndTruncatedBlocks()
    {
        var invalid = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(invalid, 3);
        Assert.Throws<FormatException>(() => DawgBlockReader.Parse(invalid));
        Assert.Throws<FormatException>(() => DawgBlockReader.Parse(new byte[5]));
    }

    [Fact]
    public void OutgoingLabels_AreBoundsCheckedAndDeterministic()
    {
        var reader = DawgBlockReader.Parse(BuildBlock(
            1u,
            1u,
            2u,
            3u));

        Assert.Equal(new byte[] { 1, 2, 3 }, reader.GetOutgoingLabels(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetOutgoingLabels(99));
    }

    [Fact]
    public void OutputLink_UsesFlagAndPackedIndex_NotTerminalWordSemantics()
    {
        var records = new uint[4];
        records[1] = 0x100u | (2u << 10);
        records[3] = 77u;
        var reader = DawgBlockReader.Parse(BuildBlock(records));

        Assert.True(reader.TryGetOutputId(1, out var outputId));
        Assert.Equal(77, outputId);
        Assert.False(reader.TryGetOutputId(0, out _));
    }

    [Fact]
    public void EveryByteAndBoundaryState_IsSafeAndNeverReturnsAnOutOfRangeTransition()
    {
        var reader = DawgBlockReader.Parse(BuildBlock(0u, 1u, 2u, 3u));

        foreach (var state in new[] { -1, 0, 1, 3, 4, int.MaxValue })
        {
            for (var value = 0; value <= byte.MaxValue; value++)
            {
                var result = reader.TryTransition(state, (byte)value, out var nextState);
                Assert.True(!result || (nextState >= 0 && nextState < reader.RecordCount));
            }
        }
    }

    [Fact]
    public void OutputLink_WithOutOfRangeTarget_IsRejectedWithoutThrowing()
    {
        var reader = DawgBlockReader.Parse(BuildBlock(0u, 0x100u | (99u << 10), 0u));

        Assert.False(reader.TryGetOutputId(1, out _));
    }

    [Fact]
    public void OutputLink_WithHighBitMarker_IsNotMistakenForAnOpaqueOutput()
    {
        var reader = DawgBlockReader.Parse(BuildBlock(0u, 0x80000100u | (2u << 10), 5u));

        Assert.False(reader.TryGetOutputId(1, out _));
    }

    private static byte[] BuildBlock(params uint[] records)
    {
        var block = new byte[(records.Length + 1) * sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(block, checked((uint)records.Length));
        return BuildBlock(records, block);
    }

    private static byte[] BuildBlock(uint[] records, byte[] block)
    {
        for (var index = 0; index < records.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                block.AsSpan((index + 1) * sizeof(uint)),
                records[index]);
        }

        return block;
    }
}
