[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [int] $ProcessId,

    [ValidateRange(1, 300)]
    [int] $DurationSeconds = 10,

    [ValidateRange(0, 1000)]
    [int] $SampleIntervalMs = 500,

    [ValidateRange(0, 1000)]
    [int] $StatusRequests = 20,

    [ValidateRange(100, 10000)]
    [int] $StatusConnectTimeoutMs = 1000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-DistributionStats {
    param(
        [Parameter(Mandatory = $true)]
        [double[]] $Values
    )

    if ($Values.Count -eq 0) {
        return [pscustomobject]@{
            Min = $null
            Average = $null
            P95 = $null
            Max = $null
        }
    }

    $sorted = @($Values | Sort-Object)
    $p95Index = [math]::Min($sorted.Count - 1, [math]::Max(0, [math]::Ceiling($sorted.Count * 0.95) - 1))
    $average = ($Values | Measure-Object -Average).Average

    return [pscustomobject]@{
        Min = [math]::Round($sorted[0], 3)
        Average = [math]::Round($average, 3)
        P95 = [math]::Round($sorted[$p95Index], 3)
        Max = [math]::Round($sorted[$sorted.Count - 1], 3)
    }
}

function Invoke-StatusProbe {
    param(
        [Parameter(Mandatory = $true)]
        [int] $ConnectTimeoutMs
    )

    $pipe = $null
    $reader = $null
    $timer = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            '.',
            'SmartInput.ResidentRuntimeStatus.v1',
            [System.IO.Pipes.PipeDirection]::In)
        $pipe.Connect($ConnectTimeoutMs)
        $reader = [System.IO.StreamReader]::new($pipe)

        # The payload is intentionally discarded. Only transport timing and
        # success/failure are retained by this diagnostic harness.
        $null = $reader.ReadToEnd()
        $timer.Stop()

        return [pscustomobject]@{
            Success = $true
            ElapsedMs = $timer.Elapsed.TotalMilliseconds
        }
    }
    catch {
        $timer.Stop()
        return [pscustomobject]@{
            Success = $false
            ElapsedMs = $null
        }
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        if ($null -ne $pipe) {
            $pipe.Dispose()
        }
    }
}

$process = Get-Process -Id $ProcessId
$process.Refresh()
$initialCpu = $process.TotalProcessorTime
$wallClock = [System.Diagnostics.Stopwatch]::StartNew()
$workingSetSamples = [System.Collections.Generic.List[double]]::new()
$privateMemorySamples = [System.Collections.Generic.List[double]]::new()

while ($wallClock.Elapsed.TotalSeconds -lt $DurationSeconds) {
    $process.Refresh()
    $workingSetSamples.Add($process.WorkingSet64 / 1MB)
    $privateMemorySamples.Add($process.PrivateMemorySize64 / 1MB)

    if ($SampleIntervalMs -gt 0) {
        Start-Sleep -Milliseconds $SampleIntervalMs
    }
}

$process.Refresh()
$wallClock.Stop()
$cpuMilliseconds = ($process.TotalProcessorTime - $initialCpu).TotalMilliseconds
$cpuPercent = if ($wallClock.Elapsed.TotalMilliseconds -gt 0) {
    100.0 * $cpuMilliseconds / $wallClock.Elapsed.TotalMilliseconds
}
else {
    0.0
}

$statusLatencies = [System.Collections.Generic.List[double]]::new()
$statusFailures = 0
for ($index = 0; $index -lt $StatusRequests; $index++) {
    $probe = Invoke-StatusProbe -ConnectTimeoutMs $StatusConnectTimeoutMs
    if ($probe.Success) {
        $statusLatencies.Add($probe.ElapsedMs)
    }
    else {
        $statusFailures++
    }
}

$workingSetStats = Get-DistributionStats -Values ([double[]] $workingSetSamples)
$privateMemoryStats = Get-DistributionStats -Values ([double[]] $privateMemorySamples)
$statusStats = Get-DistributionStats -Values ([double[]] $statusLatencies)

$result = [pscustomobject]@{
    schema = 'smartinput.resident-host-metrics.v1'
    processId = $ProcessId
    processName = $process.ProcessName
    sampleCount = $workingSetSamples.Count
    durationMs = [math]::Round($wallClock.Elapsed.TotalMilliseconds, 3)
    workingSetMiB = $workingSetStats
    privateMemoryMiB = $privateMemoryStats
    cpuProcessPercentOneCore = [math]::Round($cpuPercent, 3)
    processorCount = [Environment]::ProcessorCount
    statusPipe = [pscustomobject]@{
        requests = $StatusRequests
        successful = $statusLatencies.Count
        failures = $statusFailures
        roundTripMs = $statusStats
    }
    privacy = [pscustomobject]@{
        rawTextLogged = $false
        rawStatusPayloadLogged = $false
        windowTitlesLogged = $false
    }
}

$result | ConvertTo-Json -Depth 5 -Compress
