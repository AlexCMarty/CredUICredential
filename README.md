# CredUICredential

[![PowerShell Gallery Version](https://img.shields.io/powershellgallery/v/CredUICredential.svg)](https://www.powershellgallery.com/packages/CredUICredential)
[![PowerShell Gallery Downloads](https://img.shields.io/powershellgallery/dt/CredUICredential.svg)](https://www.powershellgallery.com/packages/CredUICredential)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

## Why do I need CredUICredential?

Although, Powershell has built-in support for getting user credentials, it leaves much to be desired.

In Powershell 7, the built-in cmdlet `Get-Credential` prompts for credentials in a terminal. It looks like this:

```
$x = Get-Credential

PowerShell credential request
Enter your credentials.
User: firstname-lastname
Password for user firstname-lastname: **********
```

There are several user experience problems with this:

- The user cannot peek their password to confirm it's correct. Powershell obscures it with asteriks.
- The user has to interact with the terminal.
- It just doesn't look good.

Overall, `Get-Credential` gets the job done, but it isn't the _best_ experience you could give your users.

## How does CredUICredential address these problems?

CredUICredential is a PowerShell module written in C# atop .NET 10. It uses P/Invoke to wrap **credui.dll**.

Because it is a binary module built for .NET 10, it needs a PowerShell host running on .NET 10: **PowerShell 7.6
or later**, on Windows.

Specifically, it calls `CredUIPromptForWindowsCredentials`, the same native API behind the modern (Vista+) Windows
credential dialog you already see for things like UAC elevation and RDP logins. Because it's a real Windows dialog
instead of a terminal prompt, you get the platform's UX for free:

- The password field has a built-in "peek" icon so the user can reveal what they typed before submitting.
- It's a native window, so it pops to the front whether the calling script is running interactively or in the background.
- It automatically matches the user's OS theme (light, dark, high contrast), because it's rendered by Windows itself.

CredUICredential exports the cmdlet `Get-CredUICredential`, a **drop-in replacement** for
`Get-Credential`: it accepts a `Credential` parameter and returns a `PSCredential`, just like the built-in cmdlet,
so in most scripts you can swap the cmdlet name and change nothing else.
On top of that baseline, it adds a couple of small conveniences:

- `-Message` lets you customize the text shown in the dialog, same as `Get-Credential`.
- `-Title` lets you customize the dialog's window caption. This is particularly useful with password managers
  like KeePass that match a window's title to decide which stored credentials to auto-type into it.
- `-ShowSaveCheckbox` adds the native "Save" check box to the dialog. When you use this switch,
  `Get-CredUICredential` returns an object with `Credential` and `Checkbox` properties instead of a bare
  `PSCredential`, since there are now two things to report back:

  ```powershell
      $result = Get-CredUICredential -ShowSaveCheckbox
      $result.Credential # a PSCredential, same as always
      $result.Checkbox   # $true or $false, depending on whether the box was checked
  ```

  Note that Windows does not let this checkbox's label be customized, and checking it doesn't save
  anything on its own — actually persisting the credential (or not) based on `$result.Checkbox` is up to
  your script.

## Usage

```powershell
    # Install module from PowerShell Gallery
    # Package URL: https://www.powershellgallery.com/packages/CredUICredential
    Install-PSResource CredUICredential
```

```powershell
    # Use the modern credential dialog
    $creds = Get-CredUICredential -Title 'Admin credentials needed' -Message 'Enter your admin credentials to continue'
```

![Modern dialog](/assets/modern.png)

The help documentation is in the `CredUICredential.md` file. Refer to the `Get-Credential` documentation for
advanced usages.

## Acknowledgement

This module is a fork of [Get-WinCredential](https://github.com/zbalkan/Get-WinCredential) by
[Zafer Balkan](https://github.com/zbalkan), archived by its original author. All credit for the original design and
implementation goes to them; this fork exists to keep the module published and maintained on the PowerShell Gallery.

The idea of the original POC stemmed from [a StackOverflow question](https://stackoverflow.com/q/70570097/5910839) by [BubblesTheTurtle](https://stackoverflow.com/users/6211486/bubblestheturtle).

The code is based on the [Credential Management API examples by Alan Dean](https://www.developerfusion.com/code/4693/using-the-credential-management-api/).
