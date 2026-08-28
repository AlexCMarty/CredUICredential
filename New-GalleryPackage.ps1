# Stages a Gallery-ready copy of the module under artifacts/CredUICredential:
# the Release DLL, generated MAML, and a manifest whose RootModule is that DLL.
# The in-repo .psd1 keeps pointing at bin/Release so local Import-Module and
# ModuleManifestTests stay unchanged.

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'CredUICredential\CredUICredential.csproj'
$dll = Join-Path $repoRoot 'CredUICredential\bin\Release\net10.0-windows\CredUICredential.dll'
$helpXml = Join-Path $repoRoot 'en-US\CredUICredential.dll-Help.xml'
$sourceManifest = Join-Path $repoRoot 'CredUICredential.psd1'
$outDir = Join-Path $repoRoot 'artifacts\CredUICredential'

& (Join-Path $repoRoot 'Update-Help.ps1')

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet build -c Release failed.'
}

if (-not (Test-Path -LiteralPath $dll)) {
    throw "Release DLL was not at '$dll'."
}
if (-not (Test-Path -LiteralPath $helpXml)) {
    throw "Generated MAML was not at '$helpXml'."
}

if (Test-Path -LiteralPath $outDir) {
    Remove-Item -LiteralPath $outDir -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $outDir 'en-US') | Out-Null

Copy-Item -LiteralPath $dll -Destination (Join-Path $outDir 'CredUICredential.dll')
Copy-Item -LiteralPath $helpXml -Destination (Join-Path $outDir 'en-US\CredUICredential.dll-Help.xml')

$stagedManifest = Join-Path $outDir 'CredUICredential.psd1'
$manifestText = Get-Content -LiteralPath $sourceManifest -Raw
$updated = [regex]::Replace(
    $manifestText,
    "(?m)^(\s*RootModule\s*=\s*)'[^']*'",
    "`$1'CredUICredential.dll'")
if ($updated -eq $manifestText) {
    throw "Could not rewrite RootModule in '$sourceManifest'."
}
Set-Content -LiteralPath $stagedManifest -Value $updated -NoNewline

Write-Host "Staged Gallery package at $outDir"
