[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]|[\\/]bin[\\/]' })
$toolFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tools') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]|[\\/]bin[\\/]' })

$sensitiveNames = 'OriginalText|ReplacementText|OriginalToken|ReplacementToken|CandidateToken|SelectedText|RawText|WindowTitle|WindowClassName|ProcessName'
$loggerCalls = 0
$unsafeLoggerCalls = [System.Collections.Generic.List[object]]::new()
$rawToolOutputs = [System.Collections.Generic.List[object]]::new()

foreach ($file in $sourceFiles) {
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        if ($lines[$lineIndex] -notmatch '\bLog(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(') {
            continue
        }

        $loggerCalls++
        $end = $lineIndex
        $endLimit = [math]::Min($lines.Count - 1, $lineIndex + 24)
        while ($end -lt $endLimit) {
            if ($lines[$end] -match '\);\s*$') {
                break
            }

            $end++
        }
        $statement = ($lines[$lineIndex..$end] -join "`n")
        if ($statement -match $sensitiveNames) {
            $unsafeLoggerCalls.Add([pscustomobject]@{
                    file = $file.FullName.Substring($projectRoot.Length + 1)
                    line = $lineIndex + 1
                    category = 'sensitive-identifier-near-log-call'
                })
        }
    }
}

foreach ($file in $toolFiles) {
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        if ($lines[$lineIndex] -notmatch 'Console\.Write(?:Line)?\s*\(') {
            continue
        }

        $end = [math]::Min($lines.Count - 1, $lineIndex + 4)
        $statement = ($lines[$lineIndex..$end] -join "`n")
        $hasInterpolatedRaw = $statement -match '\$\{\s*(?:word|candidate|replacement|original|selectedText|input|expected|actual)\b'
        $hasCandidateFormatter = $statement -match 'FormatCandidates\s*\('
        if ($hasInterpolatedRaw -or $hasCandidateFormatter) {
            $rawToolOutputs.Add([pscustomobject]@{
                    file = $file.FullName.Substring($projectRoot.Length + 1)
                    line = $lineIndex + 1
                    category = 'possible-raw-token-console-output'
                })
        }
    }
}

$factoryPath = Join-Path $projectRoot 'src\SmartInput.Core\Services\InputDiagnosticEventFactory.cs'
$eventPath = Join-Path $projectRoot 'src\SmartInput.Core\Models\InputDiagnosticEvent.cs'
$statusPath = Join-Path $projectRoot 'src\SmartInput.ResidentHost\Services\ResidentRuntimeStatusPipeService.cs'
$factoryText = Get-Content -LiteralPath $factoryPath -Raw
$eventText = Get-Content -LiteralPath $eventPath -Raw
$statusText = Get-Content -LiteralPath $statusPath -Raw

$metadataContractRedacted = $eventText -match 'ProcessName\s*=>\s*string\.Empty'
$metadataContractRedacted = $metadataContractRedacted -and ($eventText -match 'WindowTitle\s*=>\s*string\.Empty')
$metadataContractRedacted = $metadataContractRedacted -and ($eventText -match 'WindowClassName\s*=>\s*string\.Empty')
$metadataContractRedacted = $metadataContractRedacted -and ($factoryText -notmatch '\b(ProcessName|WindowTitle|WindowClassName)\s*=')
$statusContractAggregateOnly = $statusText -match 'GetSnapshot\(\)\.LivePipeline'
$statusContractAggregateOnly = $statusContractAggregateOnly -and ($statusText -notmatch '\b(OriginalText|ReplacementText|CandidateToken|RawText|WindowTitle)\b')

$privacySafe = $unsafeLoggerCalls.Count -eq 0
$privacySafe = $privacySafe -and ($rawToolOutputs.Count -eq 0)
$privacySafe = $privacySafe -and $metadataContractRedacted
$privacySafe = $privacySafe -and $statusContractAggregateOnly

$result = [pscustomobject]@{
    schema = 'smartinput.privacy-audit.v1'
    sourceFilesScanned = $sourceFiles.Count
    toolFilesScanned = $toolFiles.Count
    loggerCallsScanned = $loggerCalls
    unsafeLoggerCalls = $unsafeLoggerCalls.Count
    rawToolOutputs = $rawToolOutputs.Count
    metadataContractRedacted = $metadataContractRedacted
    statusContractAggregateOnly = $statusContractAggregateOnly
    privacySafe = $privacySafe
    rawTextLogged = $false
    rawReplacementLogged = $false
    windowTitlesLogged = $false
    findings = @($unsafeLoggerCalls + $rawToolOutputs)
}

$result | ConvertTo-Json -Depth 5 -Compress
if (-not $privacySafe) {
    exit 1
}
