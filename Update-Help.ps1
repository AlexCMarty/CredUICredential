# Regenerates en-US/CredUICredential.dll-Help.xml from CredUICredential.md.
#
# CredUICredential.md is the source of truth: edit its prose (synopsis, description, examples,
# parameter descriptions, notes) by hand. This script re-derives the structural facts - syntax,
# parameter types, mandatory/pipeline-binding flags - fresh from the built cmdlet, merges them
# with that prose, and writes the MAML. The XML is a build artifact: CI generates it before
# tests and ships it in the Gallery package. Do not commit it. Run this before `dotnet test`
# on a clean tree, and after editing the markdown or the cmdlet's parameters.
#
# Requires the Microsoft.PowerShell.PlatyPS module (installed automatically for the current user
# if missing) and a PowerShell 7.6+ host, matching the module's own minimum version.

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$markdownPath = Join-Path $repoRoot 'CredUICredential.md'
$helpOutputFolder = Join-Path $repoRoot 'en-US'
# Debug, not Release: reflection only needs the cmdlet's parameters and attributes, which are
# identical between configurations (there is no conditional compilation in the project). Using
# Debug means this script never collides with the Release-DLL import lock documented in CLAUDE.md.
$dllPath = Join-Path $repoRoot 'CredUICredential\bin\Debug\net10.0-windows\CredUICredential.dll'

if (-not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PlatyPS)) {
    Install-PSResource -Name Microsoft.PowerShell.PlatyPS -Version 1.0.3 -Scope CurrentUser -TrustRepository
}

New-Item -ItemType Directory -Path $helpOutputFolder -Force | Out-Null

dotnet build (Join-Path $repoRoot 'CredUICredential\CredUICredential.csproj') -c Debug
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed."
}

Import-Module $dllPath -Force
Import-Module Microsoft.PowerShell.PlatyPS -Force

Update-MarkdownCommandHelp -Path $markdownPath -NoBackup | Out-Null

$commandHelp = Import-MarkdownCommandHelp -Path $markdownPath
$tempOutputFolder = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $tempOutputFolder | Out-Null
try {
    Export-MamlCommandHelp -CommandHelp $commandHelp -OutputFolder $tempOutputFolder -Force | Out-Null
    $generatedXml = Get-ChildItem -Path $tempOutputFolder -Filter '*.xml' -Recurse | Select-Object -First 1
    Copy-Item -Path $generatedXml.FullName -Destination (Join-Path $helpOutputFolder 'CredUICredential.dll-Help.xml') -Force
}
finally {
    Remove-Item -Path $tempOutputFolder -Recurse -Force
}

Write-Host "Regenerated en-US/CredUICredential.dll-Help.xml from CredUICredential.md."
