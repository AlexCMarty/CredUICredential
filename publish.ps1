# Publishes the staged Gallery package. CI sets PSGALLERY_API_KEY; locally the
# key can live in .apikey (gitignored) instead.

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
& (Join-Path $repoRoot 'New-GalleryPackage.ps1')

$apiKey = $env:PSGALLERY_API_KEY
if (-not $apiKey) {
    $apiKeyPath = Join-Path $repoRoot '.apikey'
    if (-not (Test-Path -LiteralPath $apiKeyPath)) {
        throw 'Set PSGALLERY_API_KEY, or put the Gallery API key in .apikey.'
    }
    $apiKey = (Get-Content -LiteralPath $apiKeyPath -Raw).Trim()
}

Publish-PSResource -Path (Join-Path $repoRoot 'artifacts\CredUICredential') -Repository PSGallery -ApiKey $apiKey
