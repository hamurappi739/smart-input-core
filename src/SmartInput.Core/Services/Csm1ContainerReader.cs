using System.Buffers.Binary;
using System.Text;

namespace SmartInput.Core.Services;

/// <summary>
/// Structural reader for the SmartInput CSM1 model container.
/// Cryptographic verification/decryption is deliberately a separate step:
/// callers must not treat a structurally valid container as trusted.
/// </summary>
public static class Csm1ContainerReader
{
    public const ushort SupportedVersion = 1;
    public const int NonceSize = 12;
    public const int AuthenticationTagSize = 16;
    public const int SignatureSize = 64;
    public const int MaximumPlainSize = 64 * 1024 * 1024;
    public const int MaximumCompressedSize = 32 * 1024 * 1024;
    private const int FixedHeaderSize = 42;

    public static Csm1ContainerEnvelope Parse(ReadOnlySpan<byte> container)
    {
        if (container.Length < FixedHeaderSize + AuthenticationTagSize + SignatureSize)
        {
            throw new FormatException("CSM1 container is shorter than its fixed header.");
        }

        if (!container[..4].SequenceEqual("CSM1"u8))
        {
            throw new FormatException("CSM1 magic is missing.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(container[4..]);
        if (version != SupportedVersion)
        {
            throw new FormatException($"Unsupported CSM1 version: {version}.");
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(container[6..]);
        if (flags != 0)
        {
            throw new FormatException($"Unsupported CSM1 flags: 0x{flags:X4}.");
        }

        var keyId = BinaryPrimitives.ReadUInt32LittleEndian(container[8..]);
        var createdAt = BinaryPrimitives.ReadUInt64LittleEndian(container[12..]);
        var plainSize = BinaryPrimitives.ReadUInt32LittleEndian(container[20..]);
        var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(container[24..]);
        ValidateSize("plain", plainSize, MaximumPlainSize);
        ValidateSize("compressed", compressedSize, MaximumCompressedSize);

        var nonce = container.Slice(28, NonceSize).ToArray();
        var versionSize = BinaryPrimitives.ReadUInt16LittleEndian(container[40..]);
        if (versionSize is < 1 or > 128)
        {
            throw new FormatException("CSM1 model version length is outside the allowed range.");
        }

        var headerSize = checked(FixedHeaderSize + versionSize);
        if (container.Length < headerSize + AuthenticationTagSize + SignatureSize)
        {
            throw new FormatException("CSM1 container is truncated before its encrypted payload.");
        }

        var modelVersionBytes = container.Slice(FixedHeaderSize, versionSize);
        string modelVersion;
        try
        {
            modelVersion = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(modelVersionBytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new FormatException("CSM1 model version is not valid UTF-8.", exception);
        }

        var encryptedSize = checked((long)compressedSize + AuthenticationTagSize);
        var expectedLength = checked((long)headerSize + encryptedSize + SignatureSize);
        if (container.Length != expectedLength)
        {
            throw new FormatException(
                $"CSM1 length mismatch. Expected {expectedLength} bytes, got {container.Length}.");
        }

        var header = container[..headerSize].ToArray();
        var encrypted = container.Slice(headerSize, checked((int)encryptedSize)).ToArray();
        var signature = container.Slice(
            checked(headerSize + (int)encryptedSize),
            SignatureSize).ToArray();

        return new Csm1ContainerEnvelope(
            new Csm1ContainerHeader(
                version,
                flags,
                keyId,
                createdAt,
                plainSize,
                compressedSize,
                nonce,
                modelVersion,
                header),
            encrypted,
            signature);
    }

    private static void ValidateSize(string name, uint value, int maximum)
    {
        if (value == 0 || value > maximum)
        {
            throw new FormatException(
                $"CSM1 {name} size must be between 1 and {maximum} bytes.");
        }
    }
}

public sealed record Csm1ContainerHeader(
    ushort ContainerVersion,
    ushort Flags,
    uint KeyId,
    ulong CreatedAtUnixSeconds,
    uint PlainSize,
    uint CompressedSize,
    byte[] Nonce,
    string ModelVersion,
    byte[] AuthenticatedHeader);

public sealed record Csm1ContainerEnvelope(
    Csm1ContainerHeader Header,
    byte[] CiphertextAndTag,
    byte[] Signature)
{
    public byte[] SignedBytes()
    {
        var result = new byte[Header.AuthenticatedHeader.Length + CiphertextAndTag.Length];
        Buffer.BlockCopy(Header.AuthenticatedHeader, 0, result, 0, Header.AuthenticatedHeader.Length);
        Buffer.BlockCopy(
            CiphertextAndTag,
            0,
            result,
            Header.AuthenticatedHeader.Length,
            CiphertextAndTag.Length);
        return result;
    }
}
