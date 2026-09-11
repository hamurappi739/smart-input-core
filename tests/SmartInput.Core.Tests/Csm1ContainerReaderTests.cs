using System.Buffers.Binary;
using System.Text;
using SmartInput.Core.Services;

namespace SmartInput.Core.Tests;

public sealed class Csm1ContainerReaderTests
{
    [Fact]
    public void Parse_ValidContainer_ReturnsHeaderPayloadAndSignature()
    {
        var payload = Enumerable.Range(0, 11).Select(static value => (byte)value).ToArray();
        var container = BuildContainer(payload.Length, compressedSize: 7, modelVersion: "26.09.01.01");

        var parsed = Csm1ContainerReader.Parse(container);

        Assert.Equal((ushort)1, parsed.Header.ContainerVersion);
        Assert.Equal((uint)7, parsed.Header.CompressedSize);
        Assert.Equal("26.09.01.01", parsed.Header.ModelVersion);
        Assert.Equal(7 + Csm1ContainerReader.AuthenticationTagSize, parsed.CiphertextAndTag.Length);
        Assert.Equal(Csm1ContainerReader.SignatureSize, parsed.Signature.Length);
        Assert.Equal(parsed.Header.AuthenticatedHeader.Length + parsed.CiphertextAndTag.Length, parsed.SignedBytes().Length);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(Csm1ContainerReader.MaximumPlainSize + 1, 7)]
    [InlineData(7, 0)]
    [InlineData(7, Csm1ContainerReader.MaximumCompressedSize + 1)]
    public void Parse_InvalidSizes_Throws(int plainSize, int compressedSize)
    {
        var container = BuildContainer(plainSize, compressedSize, "1");

        Assert.Throws<FormatException>(() => Csm1ContainerReader.Parse(container));
    }

    [Fact]
    public void Parse_TamperedHeaderLength_IsRejected()
    {
        var container = BuildContainer(11, 7, "1");
        container[0] = (byte)'X';

        Assert.Throws<FormatException>(() => Csm1ContainerReader.Parse(container));
    }

    [Fact]
    public void Parse_InvalidUtf8ModelVersion_IsRejected()
    {
        var container = BuildContainer(11, 7, "1");
        var versionOffset = 42;
        container[versionOffset] = 0xFF;

        Assert.Throws<FormatException>(() => Csm1ContainerReader.Parse(container));
    }

    [Fact]
    public void Parse_DoesNotAcceptTrailingBytes()
    {
        var container = BuildContainer(11, 7, "1").Concat(new byte[] { 0x01 }).ToArray();

        Assert.Throws<FormatException>(() => Csm1ContainerReader.Parse(container));
    }

    private static byte[] BuildContainer(int plainSize, int compressedSize, string modelVersion)
    {
        var versionBytes = Encoding.UTF8.GetBytes(modelVersion);
        var header = new byte[42 + versionBytes.Length];
        "CSM1"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(12), 1_700_000_000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), checked((uint)plainSize));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), checked((uint)compressedSize));
        for (var index = 0; index < Csm1ContainerReader.NonceSize; index++)
        {
            header[28 + index] = (byte)(index + 1);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), checked((ushort)versionBytes.Length));
        versionBytes.CopyTo(header.AsSpan(42));

        return header
            .Concat(Enumerable.Repeat((byte)0xA5, compressedSize + Csm1ContainerReader.AuthenticationTagSize))
            .Concat(Enumerable.Repeat((byte)0x5A, Csm1ContainerReader.SignatureSize))
            .ToArray();
    }
}
