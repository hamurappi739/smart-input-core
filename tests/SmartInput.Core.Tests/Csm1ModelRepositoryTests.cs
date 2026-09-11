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

public sealed class Csm1ModelRepositoryTests
{
    [Fact]
    public async Task InstallAndReload_PublishesOnlyVerifiedSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var signingKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var container = BuildContainer(key, signingKey, "model-one", "26.09.01.01", 1_700_000_000);
        var crypto = CreateCrypto(key, signingKey);
        var path = Path.Combine(temp.Path, "model.csm");
        await using var repository = new Csm1ModelRepository(path, crypto);

        var hash = Convert.ToHexString(SHA256.HashData(container));
        Assert.True(await repository.TryInstallAsync(container, hash));
        Assert.NotNull(repository.Current);
        Assert.Equal("26.09.01.01", repository.Current!.ModelVersion);
        Assert.Equal(Encoding.UTF8.GetBytes("model-one"), repository.Current.CopyPayload());
        Assert.False(repository.Current.LoadedFromCache);
        Assert.True(File.Exists(path));

        await using var reloaded = new Csm1ModelRepository(path, crypto);
        Assert.True(await reloaded.LoadCachedAsync());
        Assert.True(reloaded.Current!.LoadedFromCache);
        Assert.Equal(repository.Current.ContainerSha256, reloaded.Current.ContainerSha256);
        Assert.Equal(repository.Current.CopyPayload(), reloaded.Current.CopyPayload());
    }

    [Fact]
    public async Task InvalidUpdate_DoesNotReplaceCurrentOrCache()
    {
        using var temp = new TemporaryDirectory();
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var signingKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var crypto = CreateCrypto(key, signingKey);
        var path = Path.Combine(temp.Path, "model.csm");
        await using var repository = new Csm1ModelRepository(path, crypto);
        var valid = BuildContainer(key, signingKey, "stable", "26.09.01.01", 1_700_000_000);
        Assert.True(await repository.TryInstallAsync(valid));
        var originalHash = repository.Current!.ContainerSha256;

        var tampered = valid.ToArray();
        tampered[^1] ^= 0x80;

        Assert.False(await repository.TryInstallAsync(tampered));
        Assert.Equal(originalHash, repository.Current!.ContainerSha256);
        Assert.Equal(valid, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CorruptCurrent_UsesLastValidRollbackCopy()
    {
        using var temp = new TemporaryDirectory();
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var signingKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var crypto = CreateCrypto(key, signingKey);
        var path = Path.Combine(temp.Path, "model.csm");
        await using (var repository = new Csm1ModelRepository(path, crypto))
        {
            Assert.True(await repository.TryInstallAsync(
                BuildContainer(key, signingKey, "first", "26.09.01.01", 1_700_000_000)));
            Assert.True(await repository.TryInstallAsync(
                BuildContainer(key, signingKey, "second", "26.09.01.02", 1_700_000_001)));
        }

        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        await using var reloaded = new Csm1ModelRepository(path, crypto);

        Assert.True(await reloaded.LoadCachedAsync());
        Assert.Equal("26.09.01.01", reloaded.Current!.ModelVersion);
        Assert.Equal(Encoding.UTF8.GetBytes("first"), reloaded.Current.CopyPayload());
    }

    [Fact]
    public async Task WrongManifestHash_IsRejectedBeforeCacheWrite()
    {
        using var temp = new TemporaryDirectory();
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var signingKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var repository = new Csm1ModelRepository(
            Path.Combine(temp.Path, "model.csm"),
            CreateCrypto(key, signingKey));
        await using (repository)
        {
            var container = BuildContainer(key, signingKey, "model", "26.09.01.01", 1_700_000_000);
            Assert.False(await repository.TryInstallAsync(container, new string('0', 64)));
            Assert.Null(repository.Current);
            Assert.False(File.Exists(Path.Combine(temp.Path, "model.csm")));
        }
    }

    [Fact]
    public async Task SnapshotPayload_IsDefensiveAgainstCallerMutation()
    {
        using var temp = new TemporaryDirectory();
        var key = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        var signingKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        await using var repository = new Csm1ModelRepository(
            Path.Combine(temp.Path, "model.csm"),
            CreateCrypto(key, signingKey));
        var container = BuildContainer(key, signingKey, "immutable", "26.09.01.01", 1_700_000_000);

        Assert.True(await repository.TryInstallAsync(container));
        var copy = repository.Current!.CopyPayload();
        copy[0] = 0;

        Assert.Equal((byte)'i', repository.Current.CopyPayload()[0]);
    }

    private static Csm1ModelCryptoService CreateCrypto(
        byte[] key,
        Ed25519PrivateKeyParameters signingKey)
        => new(
            new Dictionary<uint, byte[]> { [7] = key },
            new Ed25519ModelSignatureVerifier(signingKey.GeneratePublicKey().GetEncoded()));

    private static byte[] BuildContainer(
        byte[] key,
        Ed25519PrivateKeyParameters signingKey,
        string payloadText,
        string modelVersion,
        ulong createdAt)
    {
        var plain = Encoding.UTF8.GetBytes(payloadText);
        byte[] compressed;
        using (var compressor = new Compressor(3))
        {
            compressed = compressor.Wrap(plain).ToArray();
        }

        var versionBytes = Encoding.UTF8.GetBytes(modelVersion);
        var header = new byte[42 + versionBytes.Length];
        "CSM1"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 7);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(12), createdAt);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), checked((uint)plain.Length));
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
        var signer = new Ed25519Signer();
        signer.Init(true, signingKey);
        signer.BlockUpdate(signedBytes, 0, signedBytes.Length);
        return signedBytes.Concat(signer.GenerateSignature()).ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "smartinput-csm1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
