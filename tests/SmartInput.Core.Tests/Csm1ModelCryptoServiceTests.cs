using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using SmartInput.Core.Services;
using SmartInput.Infrastructure.Security;
using ZstdSharp;

namespace SmartInput.Core.Tests;

public sealed class Csm1ModelCryptoServiceTests
{
    [Fact]
    public void DecryptCompressedPayload_VerifiesAndDecryptsPayload()
    {
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var compressed = Encoding.UTF8.GetBytes("compressed-model-payload");
        var container = BuildSignedContainer(key, compressed, out var publicKey);
        var service = new Csm1ModelCryptoService(
            new Dictionary<uint, byte[]> { [7] = key },
            new Ed25519ModelSignatureVerifier(publicKey));

        var result = service.DecryptCompressedPayload(container, out var header);

        Assert.Equal(compressed, result);
        Assert.Equal("26.09.01.01", header.ModelVersion);
    }

    [Fact]
    public void DecryptCompressedPayload_TamperedHeader_IsRejectedBeforeDecrypt()
    {
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var container = BuildSignedContainer(key, new byte[] { 1, 2, 3 }, out var publicKey);
        container[12] ^= 0x01;
        var service = new Csm1ModelCryptoService(
            new Dictionary<uint, byte[]> { [7] = key },
            new Ed25519ModelSignatureVerifier(publicKey));

        Assert.Throws<CryptographicException>(() => service.DecryptCompressedPayload(container, out _));
    }

    [Fact]
    public void DecryptCompressedPayload_WrongKey_IsRejectedByAead()
    {
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var wrongKey = Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray();
        var container = BuildSignedContainer(key, new byte[] { 4, 5, 6 }, out var publicKey);
        var service = new Csm1ModelCryptoService(
            new Dictionary<uint, byte[]> { [7] = wrongKey },
            new Ed25519ModelSignatureVerifier(publicKey));

        Assert.Throws<CryptographicException>(() => service.DecryptCompressedPayload(container, out _));
    }

    [Fact]
    public void DecryptPayload_ZstandardRoundTrip_IsBoundedAndExact()
    {
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var plain = Enumerable.Range(0, 4096).Select(static value => (byte)(value % 251)).ToArray();
        byte[] compressed;
        using (var compressor = new Compressor(3))
        {
            compressed = compressor.Wrap(plain).ToArray();
        }

        var container = BuildSignedContainer(key, compressed, out var publicKey, plain.Length);
        var service = new Csm1ModelCryptoService(
            new Dictionary<uint, byte[]> { [7] = key },
            new Ed25519ModelSignatureVerifier(publicKey));

        var restored = service.DecryptPayload(container, out _);

        Assert.Equal(plain, restored);
    }

    private static byte[] BuildSignedContainer(
        byte[] key,
        byte[] compressed,
        out byte[] publicKey,
        int? plainSize = null)
    {
        var versionBytes = Encoding.UTF8.GetBytes("26.09.01.01");
        var header = new byte[42 + versionBytes.Length];
        "CSM1"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 7);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(12), 1_700_000_000);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header.AsSpan(20),
            checked((uint)(plainSize ?? compressed.Length)));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), checked((uint)compressed.Length));
        RandomNumberGenerator.Fill(header.AsSpan(28, 12));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), checked((ushort)versionBytes.Length));
        versionBytes.CopyTo(header.AsSpan(42));

        var ciphertext = new byte[compressed.Length];
        var tag = new byte[Csm1ContainerReader.AuthenticationTagSize];
        using (var cipher = new ChaCha20Poly1305(key))
        {
            cipher.Encrypt(header.AsSpan(28, 12), compressed, ciphertext, tag, header);
        }

        var signedBytes = header.Concat(ciphertext).Concat(tag).ToArray();
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        publicKey = privateKey.GeneratePublicKey().GetEncoded();
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(signedBytes, 0, signedBytes.Length);
        var signature = signer.GenerateSignature();

        return signedBytes.Concat(signature).ToArray();
    }
}
