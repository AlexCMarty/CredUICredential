---
Module Name: CredUICredential
Module Guid: 7d7d0c54-14b3-4f7d-9c4a-bc8673d62258
Download Help Link: https://raw.githubusercontent.com/AlexCMarty/CredUICredential/master/CredUICredential.md
Help Version: 1.2.0.0
Locale: en-US
---

# CredUICredential Module

## Synopsis

Gets a credential object based on a user name and password. It uses Windows native dialogs even on PowerShell 7.x,
instead of the terminal.

## Description

The `Get-CredUICredential` cmdlet creates a credential object for a specified user name and password. You can use the
credential object in security operations.

`Get-CredUICredential` always prompts with the modern (Vista+) Windows credential dialog: the same dialog Windows
itself shows for UAC elevation and RDP logins. Unlike `Get-Credential`, it never falls back to a terminal prompt, and
there is no registry entry that changes this.

This cmdlet aims to be a drop-in alternative to `Get-Credential`. Its parameters and its output are the same, with one
addition: `-ShowSaveCheckbox`. Refer to the `Get-Credential` documentation for advanced usages.

This module is a maintained fork of the archived [Get-WinCredential](https://github.com/zbalkan/Get-WinCredential) by
Zafer Balkan. The code is based on the [Credential Management API examples by Alan Dean](https://www.developerfusion.com/code/4693/using-the-credential-management-api/).

## Requirements

The module is a binary module built for .NET 10, so it needs a PowerShell host running on .NET 10. That means
**PowerShell 7.6 or later**, on Windows.

## Example

```powershell
    $creds = Get-CredUICredential
```

Gets a credential object and saves it in the `$creds` variable.

## Example

```powershell
    Get-CredUICredential -Message "Type your credentials"
```

Shows the dialog with a custom message.

## Example

```powershell
    Get-CredUICredential -Title "Creds" -Message "Type your credentials"
```

Shows the dialog with a custom window caption as well. The caption is useful with password managers such as KeePass
that match a window title to decide which stored credentials to auto-type.

## Example

```powershell
    Get-CredUICredential -Credential $creds
```

Returns `$creds` unchanged, without showing a dialog. This is what makes the cmdlet usable behind a
`[Parameter()][PSCredential]$Credential` of your own: pass it through, and the user is only prompted when they did not
supply one.

Note that, exactly as with `Get-Credential`, the `Credential` parameter does not accept pipeline input.
`$creds | Get-CredUICredential` does not bind.

## Example

```powershell
    $result = Get-CredUICredential -ShowSaveCheckbox
    $result.Credential
    $result.Checkbox
```

Shows the dialog with a Save check box. Since there are now two things to report, the cmdlet returns an object with
`Credential` and `Checkbox` properties instead of a bare `PSCredential`. The checkbox's label cannot be customized, and
checking it does not save anything by itself; persisting the credential is left to the caller.

## Notes

The modern CredUI dialog offers no way to pre-populate the user name field. `-UserName` is accepted for compatibility
with `Get-Credential`, but it has no effect on the dialog that is shown: the user always types their own name.
