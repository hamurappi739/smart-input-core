using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SmartInput.Infrastructure.Persistence;

/// <summary>
/// Small local-file primitive for user-owned JSON state. The destination is
/// never truncated before serialization completes, so a process interruption
/// cannot leave a partially written document at the live path.
/// </summary>
internal static class AtomicJsonFile
{
    internal const long MaxReadableBytes = 8 * 1024 * 1024;

    internal static async Task<T?> ReadAsync<T>(
        string filePath,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            return default;
        }

        if (fileInfo.Length > MaxReadableBytes)
        {
            throw new InvalidDataException("Local JSON state exceeds the safety size limit.");
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            // Atomic replacement on Windows needs delete sharing. The
            // reader still sees a stable handle while a writer publishes a
            // new inode/path, and does not observe a half-written document.
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await JsonSerializer
            .DeserializeAsync(stream, jsonTypeInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task WriteAsync<T>(
        string filePath,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer
                    .SerializeAsync(stream, value, jsonTypeInfo, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Replace(temporaryPath, filePath, destinationBackupFileName: null);
                    }
                    else
                    {
                        File.Move(temporaryPath, filePath);
                    }

                    break;
                }
                catch (IOException) when (attempt < 2)
                {
                    // Antivirus/indexer or a short-lived external reader may
                    // momentarily hold the destination. Keep the retry
                    // bounded and outside the keyboard hook.
                    await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // The successful destination write is more important than
                // cleanup telemetry; a later save can remove the orphan.
            }
        }
    }
}
