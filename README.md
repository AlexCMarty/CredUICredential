# CredUICredential

[![PowerShell Gallery Version](https://img.shields.io/powershellgallery/v/CredUICredential.svg)](https://www.powershellgallery.com/packages/CredUICredential)
[![PowerShell Gallery Downloads](https://img.shields.io/powershellgallery/dt/CredUICredential.svg)](https://www.powershellgallery.com/packages/CredUICredential)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

Gets a credential object based on a user name and password. It uses Windows native dialogs even on PowerShell 7.x, instead of terminal.

This is a maintained fork of [Get-WinCredential](https://github.com/zbalkan/Get-WinCredential) by Zafer Balkan, which
was archived by its author. 

This cmdlet aims to be a drop-in alternative to `Get-Credential`. Therefore, output is exactly the same. New parameters are included as features.
It always shows the modern (Vista+) credential dialog. However, you cannot pass the `Username` parameter as
the CREDUI API does not allow it. Another feature is the `Title` parameter that enables the user to update the caption. It can be helpful with
password management tools like KeePass which matches window title to the password.

The help documentation is in the `CredUICredential.md` file. Refer to the `Get-Credential` documentation for advanced usages.

## Usage

```powershell
    # Install module from PowerShell Gallery
    # Package URL: https://www.powershellgallery.com/packages/CredUICredential
    Install-Module CredUICredential
```

```powershell
    # Use the modern (Vista+) credential dialog
    $creds = Get-CredUICredential
```

![Modern dialog](/assets/modern.png)

## Acknowledgement

This module is a fork of [Get-WinCredential](https://github.com/zbalkan/Get-WinCredential) by
[Zafer Balkan](https://github.com/zbalkan), archived by its original author. All credit for the original design and
implementation goes to them; this fork exists to keep the module published and maintained on the PowerShell Gallery.

The idea of the original POC stemmed from [a StackOverflow question](https://stackoverflow.com/q/70570097/5910839) by [BubblesTheTurtle](https://stackoverflow.com/users/6211486/bubblestheturtle).

The code is based on the [Credential Management API examples by Alan Dean](https://www.developerfusion.com/code/4693/using-the-credential-management-api/).
