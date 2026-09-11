using System.IO.Pipes;
using System.Text.Json;
using SmartInput.Core.Integration;

namespace SmartInput.App.Services;

/// <summary>
/// Reads only the resident process' aggregate counters. It sends no token,
/// command, active-application data or other user input through the pipe.
/// </summary>
public sealed class ResidentRuntimeStatusReader : IResidentRuntimeStatusReader
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(350);

    public async Task<ResidentRuntimeStatusSnapshot?> TryReadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                ResidentRuntimeStatusIpc.PipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.ConnectAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<ResidentRuntimeStatusSnapshot>(
                    pipe,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
