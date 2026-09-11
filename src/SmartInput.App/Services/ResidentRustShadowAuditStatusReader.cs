using System.IO.Pipes;
using System.Text.Json;
using SmartInput.Core.Integration;

namespace SmartInput.App.Services;

/// <summary>
/// Reads the resident host's aggregate-only Rust audit snapshot. It has no
/// request payload and never transfers typed text to the resident process.
/// </summary>
public sealed class ResidentRustShadowAuditStatusReader : IRustShadowAuditStatusReader
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(350);

    public async Task<RustShadowAuditSnapshot?> TryReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                RustShadowAuditIpc.PipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.ConnectAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<RustShadowAuditSnapshot>(
                pipe,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
