[CmdletBinding()]
param(
    [ValidateRange(0, 120)]
    [int] $WarmupSeconds = 5,

    [ValidateRange(1, 300)]
    [int] $DurationSeconds = 15,

    [ValidateRange(10, 1000)]
    [int] $SampleIntervalMs = 250,

    [ValidateRange(0, 1000)]
    [int] $StatusRequests = 40,

    [ValidateRange(100, 10000)]
    [int] $StatusConnectTimeoutMs = 1500
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$residentExe = Join-Path $PSScriptRoot '..\..\src\SmartInput.ResidentHost\bin\x64\Release\net8.0-windows\win-x64\SmartInput.ResidentHost.exe'
$residentExe = (Resolve-Path -LiteralPath $residentExe).Path
$residentDir = Split-Path -Parent $residentExe

# Keep the Rust provider out of this resource baseline. This does not alter
# application settings and does not enable any live replacement path.
$env:SMARTINPUT_RUST_ENGINE_DLL = ''
$started = Start-Process -FilePath $residentExe -WorkingDirectory $residentDir -WindowStyle Hidden -PassThru

try {
    if ($WarmupSeconds -gt 0) {
        Start-Sleep -Seconds $WarmupSeconds
    }

    $started.Refresh()
    if ($started.HasExited) {
        throw "ResidentHost exited before the measurement started (exit code $($started.ExitCode))."
    }

    & (Join-Path $PSScriptRoot 'ResidentHostMetrics.ps1') `
        -ProcessId $started.Id `
        -DurationSeconds $DurationSeconds `
        -SampleIntervalMs $SampleIntervalMs `
        -StatusRequests $StatusRequests `
        -StatusConnectTimeoutMs $StatusConnectTimeoutMs
}
finally {
    try {
        $started.Refresh()
        if (-not $started.HasExited) {
            Stop-Process -Id $started.Id -Force
            $started.WaitForExit(5000)
        }
    }
    catch {
        # The measurement result is already aggregate-only; cleanup failures
        # must not emit process command lines or any user text.
    }
}
