using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using SmartInput.Core.Services;
using ZstdSharp;

namespace SmartInput.Infrastructure.Security;

public interface IModelSignatureVerifier
{
    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature);
}

/// <summary>
/// Verifies CSM1 signatures with the application's embedded Ed25519 public key.
/// The private signing key never belongs in the client application.
/// </summary>
public sealed class Ed25519ModelSignatureVerifier : IModelSignatureVerifier
{
    private const int PublicKeySize = 32;
    private const int SignatureSize = 64;
    private readonly byte[] _publicKey;

    public Ed25519ModelSignatureVerifier(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != PublicKeySize)
        {
            throw new ArgumentException("An Ed25519 public key must contain 32 bytes.", nameof(publicKey));
        }

        _publicKey = publicKey.ToArray();
    }

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != SignatureSize)
        {
            return false;
        }

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(_publicKey, 0));
        var bytes = data.ToArray();
        verifier.BlockUpdate(bytes, 0, bytes.Length);
        return verifier.VerifySignature(signature.ToArray());
    }
}

/// <summary>
/// Authenticates and decrypts the compressed CSM1 payload. Decompression and
/// model parsing are deliberately separate so no untrusted bytes are parsed
/// before authentication succeeds.
/// </summary>
public sealed class Csm1ModelCryptoService
{
    private const int ChaChaKeySize = 32;
    private readonly IReadOnlyDictionary<uint, byte[]> _symmetricKeys;
    private readonly IModelSignatureVerifier _signatureVerifier;

    public Csm1ModelCryptoService(
        IReadOnlyDictionary<uint, byte[]> symmetricKeys,
        IModelSignatureVerifier signatureVerifier)
    {
        ArgumentNullException.ThrowIfNull(symmetricKeys);
        _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
        _symmetricKeys = symmetricKeys.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray());
    }

    public byte[] DecryptCompressedPayload(
        ReadOnlySpan<byte> container,
        out Csm1ContainerHeader header)
    {
        var envelope = Csm1ContainerReader.Parse(container);
        header = envelope.Header;

        if (!_signatureVerifier.Verify(envelope.SignedBytes(), envelope.Signature))
        {
            throw new CryptographicException("CSM1 signature verification failed.");
        }

        if (!_symmetricKeys.TryGetValue(header.KeyId, out var key)
            || key.Length != ChaChaKeySize)
        {
            throw new CryptographicException("No valid symmetric key is available for the CSM1 key id.");
        }

        var ciphertextLength = checked((int)header.CompressedSize);
        var ciphertext = envelope.CiphertextAndTag.AsSpan(0, ciphertextLength);
        var tag = envelope.CiphertextAndTag.AsSpan(ciphertextLength, Csm1ContainerReader.AuthenticationTagSize);
        var compressed = new byte[ciphertextLength];

        try
        {
            using var cipher = new ChaCha20Poly1305(key);
            cipher.Decrypt(
                header.Nonce,
                ciphertext,
                tag,
                compressed,
                header.AuthenticatedHeader);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException("CSM1 payload authentication failed.", exception);
        }

        return compressed;
    }

    public byte[] DecryptPayload(
        ReadOnlySpan<byte> container,
        out Csm1ContainerHeader header)
    {
        var compressed = DecryptCompressedPayload(container, out header);
        return Csm1ZstdPayloadDecoder.Decompress(
            compressed,
            checked((int)header.PlainSize));
    }
}

internal static class Csm1ZstdPayloadDecoder
{
    internal static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedPlainSize)
    {
        if (expectedPlainSize <= 0
            || expectedPlainSize > Csm1ContainerReader.MaximumPlainSize)
        {
            throw new FormatException("CSM1 plain size is outside the allowed range.");
        }

        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var decompressor = new DecompressionStream(input);
        using var output = new LimitedWriteStream(expectedPlainSize);
        try
        {
            decompressor.CopyTo(output);
        }
        catch (InvalidDataException exception)
        {
            throw new FormatException("CSM1 Zstandard payload is invalid.", exception);
        }

        if (output.Length != expectedPlainSize)
        {
            throw new FormatException(
                $"CSM1 plain size mismatch. Expected {expectedPlainSize}, got {output.Length}.");
        }

        return output.ToArray();
    }

    private sealed class LimitedWriteStream(int maximumLength) : Stream
    {
        private readonly MemoryStream _inner = new(capacity: maximumLength);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            _inner.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureCapacity(count);
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        public byte[] ToArray() => _inner.ToArray();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void EnsureCapacity(int count)
        {
            if (count < 0 || Length > maximumLength - count)
            {
                throw new InvalidDataException("CSM1 Zstandard output exceeds the declared limit.");
            }
        }
    }
}
