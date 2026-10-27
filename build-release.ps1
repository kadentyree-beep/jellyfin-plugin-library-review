param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
& $Dotnet build "$PSScriptRoot/src/LibraryReview" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$out = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dll = Join-Path $PSScriptRoot 'src/LibraryReview/bin/Release/net10.0/Jellyfin.Plugin.LibraryReview.dll'
$zip = Join-Path $out 'library-review-0.1.1-alpha.2.zip'
Compress-Archive -LiteralPath $dll -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm MD5).Hash.ToLowerInvariant()
$manifest = @(@{
    guid = '9eba6ec4-c190-40c2-961c-71e92174680e'
    name = 'Library Review'
    overview = 'Experimental movie metadata review; no automatic deletion.'
    description = 'Alpha for Jellyfin 12.2.0. Admin review page with optional explicitly confirmed exact-file replacement. Read the safety documentation before enabling replacement.'
    owner = 'kadentyree-beep'
    category = 'General'
    versions = @(@{
        version = '0.1.1.0'
        changelog = 'Disposable Jellyfin/Radarr integration verified. Supported authentication, blocklist verification and multi-edition safeguards. Replacement remains disabled by default.'
        targetAbi = '12.2.0.0'
        sourceUrl = 'https://github.com/kadentyree-beep/jellyfin-plugin-library-review/releases/download/v0.1.1-alpha.2/library-review-0.1.1-alpha.2.zip'
        checksum = $hash
        timestamp = [DateTime]::UtcNow.ToString('o')
    })
})
ConvertTo-Json -InputObject $manifest -Depth 8 | Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding utf8
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  library-review-0.1.1-alpha.2.zip" } | Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt')
Write-Host "Package ready in $out"
