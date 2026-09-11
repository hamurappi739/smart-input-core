using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using SmartInput.Core.Integration;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Exposes only one aggregate audit snapshot to the settings process. The
/// server never accepts a token, context or command, so it cannot become a
/// second text-input path.
/// </summary>
internal sealed class RustShadowAuditStatusPipeService : BackgroundService
{
    private readonly IRustShadowAuditService _audit;

    public RustShadowAuditStatusPipeService(IRustShadowAuditService audit)
    {
        _audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    RustShadowAuditIpc.PipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                var snapshot = new RustShadowAuditSnapshot(_audit.ProviderState, _audit.Status);
                await JsonSerializer.SerializeAsync(
                    pipe,
                    snapshot,
                    RustShadowAuditJsonContext.Default.RustShadowAuditSnapshot,
                    stoppingToken)
                    .ConfigureAwait(false);
                await pipe.FlushAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A settings window may close while receiving a snapshot.
                // There is no user text or actionable state to retain.
            }
        }
    }
}
