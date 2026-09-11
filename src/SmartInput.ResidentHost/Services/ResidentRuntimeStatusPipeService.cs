using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using SmartInput.App.Services;
using SmartInput.Core.Integration;
using SmartInput.Platform.Abstractions.Diagnostics;

namespace SmartInput.ResidentHost.Services;

/// <summary>
/// Makes a one-way, aggregate-only runtime health snapshot available to the
/// settings window. No caller can send text, commands or target-app metadata
/// through this endpoint.
/// </summary>
internal sealed class ResidentRuntimeStatusPipeService : BackgroundService
{
    private readonly IInputDiagnosticCoordinator _diagnostics;
    private readonly ILiveLayoutCorrectionCoordinator _layout;
    private readonly IPerformanceMetricsRecorder _performanceMetrics;

    public ResidentRuntimeStatusPipeService(
        IInputDiagnosticCoordinator diagnostics,
        ILiveLayoutCorrectionCoordinator layout,
        IPerformanceMetricsRecorder performanceMetrics)
    {
        _diagnostics = diagnostics;
        _layout = layout;
        _performanceMetrics = performanceMetrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    ResidentRuntimeStatusIpc.PipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                var snapshot = new ResidentRuntimeStatusSnapshot(
                    _diagnostics.IsMonitoring,
                    _layout.Status,
                    _performanceMetrics.GetSnapshot().LivePipeline);
                await JsonSerializer.SerializeAsync(
                        pipe,
                        snapshot,
                        ResidentRuntimeStatusJsonContext.Default.ResidentRuntimeStatusSnapshot,
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
                // The settings window may close while reading the snapshot.
            }
        }
    }
}
