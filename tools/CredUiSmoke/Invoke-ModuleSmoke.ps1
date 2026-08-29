<#
.SYNOPSIS
    End-to-end smoke test of Get-CredUICredential with nobody at the keyboard.

.DESCRIPTION
    Starts a separate PowerShell process that imports the built module and calls the cmdlet, then
    uses CredUiSmoke.exe to find that process's credential dialog, type CREDUI_SMOKE_PASSWORD into
    it and click OK. Reports what the cmdlet produced: the type of the output, the user name, the
    LENGTH of the password (never the password), and any error records.

    CredUI does not validate the password, so CREDUI_SMOKE_PASSWORD can be any throwaway string.
    Nothing is logged but its length.

.PARAMETER Repo
    The module repository. Defaults to the one this script lives in.

.PARAMETER Arguments
    Extra arguments for the cmdlet, e.g. '-ShowSaveCheckbox'.

.PARAMETER Cancel
    Cancel the dialog instead of submitting it, to check the cancel path.

.PARAMETER Screenshot
    Save PNGs of the dialog the cmdlet raised - the one surface the xunit suite cannot see. Only
    the dialog's own window is captured, and a typed password shows on it as dots.

.PARAMETER ScreenshotDirectory
    Where those PNGs go. Defaults to %TEMP%\CredUiSmoke\<timestamp>-<pid>.

.PARAMETER TimeoutSeconds
    Hard ceiling on the whole run. The child process is killed if it overruns, so this script
    cannot hang on a modal dialog.

.EXAMPLE
    $env:CREDUI_SMOKE_PASSWORD = 'throwaway'
    pwsh -NoProfile -File tools\CredUiSmoke\Invoke-ModuleSmoke.ps1
#>
[CmdletBinding()]
param(
    [string] $Repo = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string] $Arguments = '',
    [switch] $Cancel,
    [switch] $Screenshot,
    [string] $ScreenshotDirectory,
    [int]    $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
$harness = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\CredUiSmoke.exe'

if (-not (Test-Path $harness)) {
    throw "Build the harness first: dotnet build -c Release '$PSScriptRoot'"
}

if (-not $Cancel -and -not $env:CREDUI_SMOKE_PASSWORD) {
    throw 'Set CREDUI_SMOKE_PASSWORD (any throwaway string; CredUI does not validate it).'
}

$manifest = Join-Path $Repo 'CredUICredential.psd1'
if (-not (Test-Path $manifest)) { throw "No module manifest at $manifest" }

$outputFile = Join-Path ([System.IO.Path]::GetTempPath()) "credui-smoke-$PID.json"
Remove-Item $outputFile -ErrorAction SilentlyContinue

# The dialog window belongs to CredentialUIBroker.exe, not to the child PowerShell, so the driver
# cannot find it by process id. The message text is the one thing the caller controls that reaches
# the window, so it has to be unique per run.
$label = "CredUiSmoke end-to-end $PID"

# The child reports the shape of what it got, never the secret itself.
$childScript = @"
`$ErrorActionPreference = 'Continue'
Import-Module '$manifest' -Force
`$errors = @()
`$result = Get-CredUICredential -Message '$label' $Arguments -ErrorVariable errors -ErrorAction SilentlyContinue
`$credential = if (`$result -is [System.Management.Automation.PSCredential]) { `$result }
              elseif (`$result -and `$result.PSObject.Properties['Credential']) { `$result.Credential }
              else { `$null }
`$report = [ordered]@{
    outputType     = if (`$result) { `$result.GetType().FullName } else { '<none>' }
    hasCredential  = [bool]`$credential
    userName       = if (`$credential) { `$credential.UserName } else { '' }
    passwordLength = if (`$credential) { `$credential.GetNetworkCredential().Password.Length } else { -1 }
    checkbox       = if (`$result -and `$result.PSObject.Properties['Checkbox']) { `$result.Checkbox } else { `$null }
    errorIds       = @(`$errors | ForEach-Object { `$_.FullyQualifiedErrorId })
    errorMessages  = @(`$errors | ForEach-Object { `$_.Exception.Message })
}
`$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath '$outputFile' -Encoding UTF8
"@

$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childScript))
$child = Start-Process -FilePath 'pwsh' `
    -ArgumentList '-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded `
    -PassThru -WindowStyle Hidden

Write-Host "Child PowerShell pid=$($child.Id); waiting for a dialog showing '$label' ..."

$driveArgs = @('drive', '--label', $label, '--timeout', [Math]::Max(20, $TimeoutSeconds - 30))
if ($Cancel) { $driveArgs += '--cancel' }
if ($Screenshot -or $ScreenshotDirectory) { $driveArgs += '--shot' }
if ($ScreenshotDirectory) { $driveArgs += @('--shot-dir', $ScreenshotDirectory) }

& $harness @driveArgs
$driveExit = $LASTEXITCODE
Write-Host "driver exit=$driveExit"

if (-not $child.WaitForExit($TimeoutSeconds * 1000)) {
    Write-Warning "Child did not exit within ${TimeoutSeconds}s; killing it (a dialog is probably still up)."
    $child.Kill($true)
    exit 3
}

if (-not (Test-Path $outputFile)) {
    Write-Warning 'The child produced no report.'
    exit 4
}

Write-Host '=== cmdlet result ==='
Get-Content -LiteralPath $outputFile -Raw
Remove-Item $outputFile -ErrorAction SilentlyContinue
exit $driveExit
