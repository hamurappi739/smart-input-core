[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts\SmartInput.Core.Release.2026-09-08'
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if ($outputRoot -eq $projectRoot -or $outputRoot.StartsWith($projectRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -eq $false) {
    throw 'OutputDirectory must be a child of the project root.'
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$fileCopies = @(
    @{ Source = 'SmartInput.sln'; Destination = 'SmartInput.sln' },
    @{ Source = 'docs\FULL_END_TO_END_LIVE_AUDIT_RU.md'; Destination = 'docs\FULL_END_TO_END_LIVE_AUDIT_RU.md' },
    @{ Source = 'docs\SMART_INPUT_CORE_HANDOFF_RU.md'; Destination = 'docs\SMART_INPUT_CORE_HANDOFF_RU.md' },
    @{ Source = 'docs\SMART_INPUT_CORE_RELEASE_HANDOFF_RU.md'; Destination = 'docs\SMART_INPUT_CORE_RELEASE_HANDOFF_RU.md' },
    @{ Source = 'docs\CORE_SCORER_INTEGRATION_RU.md'; Destination = 'docs\CORE_SCORER_INTEGRATION_RU.md' },
    @{ Source = 'docs\RUST_SHADOW_MODE_RU.md'; Destination = 'docs\RUST_SHADOW_MODE_RU.md' },
    @{ Source = 'docs\MEMORY_OPTIMIZATION_PLAN_RU.md'; Destination = 'docs\MEMORY_OPTIMIZATION_PLAN_RU.md' },
    @{ Source = 'docs\BLOCK_2_RACE_BOUNDARY_AUDIT_RU.md'; Destination = 'docs\BLOCK_2_RACE_BOUNDARY_AUDIT_RU.md' },
    @{ Source = 'docs\BLOCK_3_RESOURCE_AUDIT_RU.md'; Destination = 'docs\BLOCK_3_RESOURCE_AUDIT_RU.md' },
    @{ Source = 'docs\BLOCK_4_PRIVACY_TELEMETRY_AUDIT_RU.md'; Destination = 'docs\BLOCK_4_PRIVACY_TELEMETRY_AUDIT_RU.md' },
    @{ Source = 'docs\BLOCK_5_RELEASE_PACKAGE_AUDIT_RU.md'; Destination = 'docs\BLOCK_5_RELEASE_PACKAGE_AUDIT_RU.md' },
    @{ Source = 'tools\PrivacyAudit\PrivacyAudit.ps1'; Destination = 'tools\PrivacyAudit\PrivacyAudit.ps1' },
    @{ Source = 'tools\ResidentHostMetrics\ResidentHostMetrics.ps1'; Destination = 'tools\ResidentHostMetrics\ResidentHostMetrics.ps1' },
    @{ Source = 'tools\ResidentHostMetrics\Run-ResidentHostMetrics.ps1'; Destination = 'tools\ResidentHostMetrics\Run-ResidentHostMetrics.ps1' },
    @{ Source = 'tools\WordProbe\Program.cs'; Destination = 'tools\WordProbe\Program.cs' },
    @{ Source = 'tools\WordProbe\WordProbe.csproj'; Destination = 'tools\WordProbe\WordProbe.csproj' },
    @{ Source = 'tools\ScorerAudit\Program.cs'; Destination = 'tools\ScorerAudit\Program.cs' },
    @{ Source = 'tools\ScorerAudit\ScorerAudit.csproj'; Destination = 'tools\ScorerAudit\ScorerAudit.csproj' },
    @{ Source = 'tools\ScorerAudit\heldout.ru.tsv'; Destination = 'tools\ScorerAudit\heldout.ru.tsv' },
    @{ Source = 'tools\ReleasePackaging\PackageRelease.ps1'; Destination = 'tools\ReleasePackaging\PackageRelease.ps1' }
)

foreach ($copy in $fileCopies) {
    $sourcePath = Join-Path $projectRoot $copy.Source
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required package file is missing: $($copy.Source)"
    }
    $destinationPath = Join-Path $outputRoot $copy.Destination
    New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}

foreach ($sourceDirectory in @('src', 'tests')) {
    $sourceRoot = Join-Path $projectRoot $sourceDirectory
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -notin @('.dmp', '.mdmp', '.log')
    } | ForEach-Object {
        $relativePath = $_.FullName.Substring($projectRoot.Length + 1)
        $destinationPath = Join-Path $outputRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
    }
}

$releaseCopies = @(
    @{ Source = 'src\SmartInput.ResidentHost\bin\x64\Release\net8.0-windows\win-x64'; Destination = 'bin\ResidentHost' },
    @{ Source = 'tools\WordProbe\bin\Release\net8.0'; Destination = 'bin\WordProbe' },
    @{ Source = 'tools\ScorerAudit\bin\Release\net8.0-windows'; Destination = 'bin\ScorerAudit' }
)

foreach ($release in $releaseCopies) {
    $sourcePath = Join-Path $projectRoot $release.Source
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "Release output is missing: $($release.Source)"
    }
    $destinationPath = Join-Path $outputRoot $release.Destination
    New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
    Get-ChildItem -LiteralPath $sourcePath -Force | Copy-Item -Destination $destinationPath -Recurse -Force
}

$manifest = Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Where-Object {
    $_.Name -ne 'MANIFEST.sha256.json'
} | Sort-Object FullName | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    [pscustomobject]@{
        path = $_.FullName.Substring($outputRoot.Length + 1).Replace('\', '/')
        bytes = $_.Length
        sha256 = $hash.Hash.ToLowerInvariant()
    }
}

$manifestPath = Join-Path $outputRoot 'MANIFEST.sha256.json'
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

[pscustomobject]@{
    schema = 'smartinput.release-package.v1'
    output = $outputRoot
    files = @($manifest).Count
    bytes = [long](($manifest | Measure-Object -Property bytes -Sum).Sum)
    sha256Manifest = $manifestPath
    liveReplacement = $false
    rustMode = 'audit-shadow-only'
} | ConvertTo-Json -Compress
