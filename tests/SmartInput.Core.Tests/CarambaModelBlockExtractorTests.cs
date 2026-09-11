using System.Buffers.Binary;
using SmartInput.Infrastructure.Security;

namespace SmartInput.Core.Tests;

public sealed class CarambaModelBlockExtractorTests
{
    [Fact]
    public void Extract_UsesConfirmedNestedFieldPaths()
    {
        var blockA = BuildBlock(0u, 1u, 2u);
        var blockB = BuildBlock(1u, 1u, 2u, 3u);
        var payload = EncodeField(
            2,
            EncodeField(2, blockA).Concat(new byte[] { 0x18, 0x01 }).ToArray())
            .Concat(EncodeField(5, EncodeField(1, blockB)))
            .ToArray();

        var blocks = CarambaModelBlockExtractor.Extract(payload);

        Assert.Equal(blockA, blocks.BlockA);
        Assert.Equal(blockB, blocks.BlockB);
    }

    [Fact]
    public void Extract_RejectsMissingOrMalformedNestedFields()
    {
        Assert.Throws<FormatException>(() =>
            CarambaModelBlockExtractor.Extract(EncodeField(2, new byte[] { 0x12, 0x01, 0x00 })));

        var malformed = EncodeField(2, EncodeField(2, new byte[] { 1, 2, 3 }));
        malformed[^1] = 0xFF;
        Assert.Throws<FormatException>(() => CarambaModelBlockExtractor.Extract(malformed));
    }

    private static byte[] BuildBlock(params uint[] records)
    {
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

    private static byte[] EncodeField(int fieldNumber, byte[] value)
    {
        using var stream = new MemoryStream();
        WriteVarint(stream, checked((ulong)fieldNumber << 3 | 2));
        WriteVarint(stream, checked((ulong)value.Length));
        stream.Write(value);
        return stream.ToArray();
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }
}
