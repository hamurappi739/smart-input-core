using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SmartInput.Core.Services;

namespace SmartInput.Infrastructure.Security;

/// <summary>
/// Installs only authenticated CSM1 models and keeps the previous valid
/// container as a rollback copy. This repository is not wired into correction
/// decisions until the Caramba lexicon adapter has passed comparison tests.
/// </summary>
public sealed class Csm1ModelRepository : IModelRepository, IAsyncDisposable
{
    private const int Sha256HexLength = 64;
    private static readonly int MaximumContainerSize = checked(
        42
        + 128
        + Csm1ContainerReader.MaximumCompressedSize
        + Csm1ContainerReader.AuthenticationTagSize
        + Csm1ContainerReader.SignatureSize);

    private readonly string _cachePath;
    private readonly string _backupPath;
    private readonly Csm1ModelCryptoService _crypto;
    private readonly ILogger<Csm1ModelRepository>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Csm1ModelSnapshot? _current;

    public Csm1ModelRepository(
        string cachePath,
        Csm1ModelCryptoService crypto,
        ILogger<Csm1ModelRepository>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            throw new ArgumentException("A model cache path is required.", nameof(cachePath));
        }

        _cachePath = Path.GetFullPath(cachePath);
        _backupPath = _cachePath + ".bak";
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _logger = logger;
    }

    public Csm1ModelSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Authenticates and decrypts the new model before touching the cache.
    /// Returns false for an invalid update; the previous model remains active.
    /// </summary>
    public async Task<bool> TryInstallAsync(
        ReadOnlyMemory<byte> container,
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!TryValidateHash(container.Span, expectedSha256))
            {
                LogRejected("hash");
                return false;
            }

            if (container.Length > MaximumContainerSize)
            {
                LogRejected("size");
                return false;
            }

            Csm1ContainerHeader header;
            byte[] payload;
            try
            {
                payload = _crypto.DecryptPayload(container.Span, out header);
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                and not OutOfMemoryException
                and not StackOverflowException)
            {
                _logger?.LogWarning("Rejected CSM1 model during verification ({Reason}).",
                    exception.GetType().Name);
                return false;
            }

            var hash = Convert.ToHexString(SHA256.HashData(container.Span));
            var snapshot = new Csm1ModelSnapshot(
                header.ModelVersion,
                header.KeyId,
                header.CreatedAtUnixSeconds,
                payload,
                hash,
                loadedFromCache: false);

            await WriteAtomicallyAsync(container, cancellationToken).ConfigureAwait(false);
            _current = snapshot;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads the current cache, then the rollback copy if needed.</summary>
    public async Task<bool> LoadCachedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryLoadFile(_cachePath, out var current))
            {
                _current = current;
                return true;
            }

            if (TryLoadFile(_backupPath, out var rollback))
            {
                _current = rollback;
                return true;
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private bool TryLoadFile(string path, out Csm1ModelSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumContainerSize)
            {
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            var payload = _crypto.DecryptPayload(bytes, out var header);
            snapshot = new Csm1ModelSnapshot(
                header.ModelVersion,
                header.KeyId,
                header.CreatedAtUnixSeconds,
                payload,
                Convert.ToHexString(SHA256.HashData(bytes)),
                loadedFromCache: true);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not OutOfMemoryException
            and not StackOverflowException)
        {
            _logger?.LogWarning("Rejected cached CSM1 model ({Reason}).", exception.GetType().Name);
            return false;
        }
    }

    private async Task WriteAtomicallyAsync(
        ReadOnlyMemory<byte> container,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_cachePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException("The model cache path has no directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.SequentialScan))
            {
                await stream.WriteAsync(container, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_cachePath))
            {
                File.Move(_cachePath, _backupPath, overwrite: true);
            }

            try
            {
                File.Move(temporaryPath, _cachePath);
            }
            catch
            {
                if (File.Exists(_backupPath) && !File.Exists(_cachePath))
                {
                    File.Move(_backupPath, _cachePath);
                }

                throw;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryValidateHash(ReadOnlySpan<byte> container, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return true;
        }

        if (expectedSha256.Length != Sha256HexLength)
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedSha256);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != 32)
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[32];
        SHA256.HashData(container, actual);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private void LogRejected(string reason)
        => _logger?.LogWarning("Rejected CSM1 model ({Reason}).", reason);
}
