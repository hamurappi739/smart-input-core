[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $env:LOCALAPPDATA 'SmartInput\Dictionaries')
)

$ErrorActionPreference = 'Stop'

# The application reads these files locally. This script is the only part that
# downloads them; normal SmartInput runtime never performs network requests.
$packs = @(
    @{
        Language = 'Russian'
        Files = @(
            @{ Name = 'ru_RU.aff'; Url = 'https://raw.githubusercontent.com/Goudron/ru-spelling-dictionary/main/ru_RU.aff'; MinimumBytes = 10000 }
            @{ Name = 'ru_RU.dic'; Url = 'https://raw.githubusercontent.com/Goudron/ru-spelling-dictionary/main/ru_RU.dic'; MinimumBytes = 1000000 }
        )
        LicenseUrl = 'https://github.com/Goudron/ru-spelling-dictionary/blob/main/LICENSE'
    }
    @{
        Language = 'English'
        Files = @(
            @{ Name = 'en_US.aff'; Url = 'https://raw.githubusercontent.com/facelessuser/hunspell-en-us/master/en_US.aff'; MinimumBytes = 1000 }
            # The normal SCOWL-60 English dictionary is smaller than the
            # Russian morphology dictionary, but still must not be an HTML or
            # empty error response.
            @{ Name = 'en_US.dic'; Url = 'https://raw.githubusercontent.com/facelessuser/hunspell-en-us/master/en_US.dic'; MinimumBytes = 100000 }
        )
        LicenseUrl = 'https://raw.githubusercontent.com/facelessuser/hunspell-en-us/master/README_en_US.txt'
    }
)

New-Item -ItemType Directory -Path $Destination -Force | Out-Null

foreach ($pack in $packs) {
    $staging = Join-Path $Destination ('.staging-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    try {
        foreach ($file in $pack.Files) {
            $target = Join-Path $staging $file.Name
            Invoke-WebRequest -UseBasicParsing -Uri $file.Url -OutFile $target
            $length = (Get-Item -LiteralPath $target).Length
            if ($length -lt $file.MinimumBytes) {
                throw "Downloaded dictionary file is unexpectedly small: $($file.Name)"
            }

            if ($pack.Language -eq 'Russian') {
                # Goudron publishes the Russian pair as KOI8-R. Convert it in
                # the staging directory so the runtime can consume UTF-8.
                $koi8 = [System.Text.Encoding]::GetEncoding(20866)
                $utf8 = [System.Text.UTF8Encoding]::new($false)
                $content = [System.IO.File]::ReadAllText($target, $koi8)
                if ($file.Name -eq 'ru_RU.aff') {
                    $content = $content -replace '^SET KOI8-R', 'SET UTF-8'
                }
                [System.IO.File]::WriteAllText($target, $content, $utf8)
            }
        }

        foreach ($file in $pack.Files) {
            Move-Item -LiteralPath (Join-Path $staging $file.Name) -Destination (Join-Path $Destination $file.Name) -Force
        }

        $licensePath = Join-Path $Destination ($pack.Language + '-LICENSE.txt')
        Set-Content -LiteralPath $licensePath -Value @(
            "Source: $($pack.LicenseUrl)"
            "Downloaded: $(Get-Date -Format o)"
            ""
            'Review the upstream license before redistributing dictionary files.'
        ) -Encoding UTF8
    }
    finally {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
    }
}

Write-Host "Hunspell dictionaries installed in: $Destination"
Write-Host 'Restart SmartInput to load the new files.'
