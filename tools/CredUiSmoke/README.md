# CredUiSmoke

A smoke-test harness for `CredUIPromptForWindowsCredentials`, for the `CredUICredential`
PowerShell module. It drives the real dialog with nobody at the keyboard, which the xunit suite
deliberately cannot do — the fakes in `tests/` replace the native prompt, so nothing there sees
the credential provider UI that the peek glyph and "More choices" belong to.

Developer tooling, not shipped: it is not in `CredUICredential.sln`, so `dotnet build` and CI do
not build it, and `New-GalleryPackage.ps1` does not package it. Build it on demand.

## Build

```powershell
dotnet build -c Release tools\CredUiSmoke
```

The executable is `tools\CredUiSmoke\bin\Release\net10.0-windows\CredUiSmoke.exe`.

## Why it cannot hang

Every command that opens the dialog also knows how to close it: a `--timeout` (45s by default,
300s for `submit`) after which the Cancel button is invoked, and a watchdog thread that calls
`Environment.Exit` a minute later if the prompt still has not returned. The prompt runs on a
background STA thread, so the process can exit out from under a dialog that will not close.

## Secrets

Passwords and PINs are read from `CREDUI_SMOKE_PASSWORD` and `CREDUI_SMOKE_PIN` and are never
printed. A decoded password is reported only as a length and a character-class histogram, which
is enough to tell real text from mojibake without disclosing it.

Screenshots are of the credential dialog's own window and nothing else on the desktop, and the
dialog never renders a typed password or PIN as anything but dots. `--shot-screen` captures the
whole desktop instead, which is a deliberate choice to make, not a default.

CredUI does not validate what is typed, so `CREDUI_SMOKE_PASSWORD` can be any throwaway string.
That is not true of a PIN: a wrong one counts against Windows Hello's own failure counter, so the
harness never guesses one, and `pin --no-submit` stops at the PIN field.

## Commands

`CredUiSmoke.exe --help` lists them all. The useful ones:

```powershell
$h = "tools\CredUiSmoke\bin\Release\net10.0-windows\CredUiSmoke.exe"

# LSA package ids. No dialog.
& $h packages

# Open the dialog, dump the UI Automation tree, expand "More choices", list the tiles, Cancel.
& $h enumerate --more --type-probe --user $env:USERNAME

# Full automated OK submit. Reports return code, out auth package, buffer shape, unpack result.
$env:CREDUI_SMOKE_PASSWORD = 'throwaway'
& $h auto

# Does seeding pulAuthPackage filter the providers?
& $h enumerate --in-package Kerberos --more

# A picture of it. Writes PNGs and cancels; nobody has to be at the keyboard.
& $h shot --user $env:USERNAME --more --type-probe --shot-dir .\shots

# One-shot human diagnostic: raise the prompt, let somebody submit it, report what came back.
& $h submit --in-package 3 --timeout 300
```

## Screenshots

The automation tree says which elements exist. It does not say what the credential provider drew,
and the peek glyph and the "More choices" tiles are precisely what no test in `tests/` can see, so
claims about them are worth a picture.

`shot` opens the dialog, captures it, and cancels: `--type-probe` adds a capture with the password
box filled, because the XAML `PasswordBox` only templates its reveal button once there is something
to reveal, and `--more` adds one of the expanded tile list. `--shot` does the same for any other
command that opens a dialog - `enumerate`, `auto`, `pin`, `submit`, `drive` - capturing at each
point that command already reports on.

```powershell
# The module's own seeding, which should leave no PIN or smart-card tile under More choices.
& $h shot --in-package Kerberos --user $env:USERNAME --more
```

PNGs land in `%TEMP%\CredUiSmoke\<timestamp>-<pid>` unless `--shot-dir` says otherwise, numbered
in the order they were taken; every path is printed. Capture is per-thread DPI aware, so the
rectangle is right on a scaled display. A capture that comes back a single flat colour is called
out as such: a `CREDUIWIN_SECURE_PROMPT` dialog lives on the secure desktop and cannot be
photographed from here.

## End to end, through the real cmdlet

`Invoke-ModuleSmoke.ps1` starts a separate PowerShell that imports the built module and calls
`Get-CredUICredential`, drives its dialog, and reports what the cmdlet produced. It defaults to
the repository it sits in, so it needs no arguments.

```powershell
$env:CREDUI_SMOKE_PASSWORD = 'throwaway'
pwsh -NoProfile -File tools\CredUiSmoke\Invoke-ModuleSmoke.ps1 -Arguments '-ShowSaveCheckbox'
pwsh -NoProfile -File tools\CredUiSmoke\Invoke-ModuleSmoke.ps1 -Cancel
pwsh -NoProfile -File tools\CredUiSmoke\Invoke-ModuleSmoke.ps1 -Screenshot
```

The manifest it imports is the in-repo one, whose `RootModule` points at `bin/Release`, so build
the module in Release first.

It finds the dialog by its message text, not by process id: on Windows 11 the credential dialog is
drawn by `CredentialUIBroker.exe`, so the window does not belong to the process that called
`CredUIPromptForWindowsCredentials`.
