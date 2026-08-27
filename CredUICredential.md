---
document type: cmdlet
external help file: CredUICredential.dll-Help.xml
HelpUri: https://github.com/AlexCMarty/CredUICredential/blob/master/CredUICredential.md
Locale: en-US
Module Guid: 7d7d0c54-14b3-4f7d-9c4a-bc8673d62258
Module Name: CredUICredential
ms.date: 08/27/2026
PlatyPS schema version: 2024-05-01
title: Get-CredUICredential
---

# Get-CredUICredential

## SYNOPSIS

Gets a credential object based on a user name and password. It uses Windows native dialogs even on
PowerShell 7.x, instead of the terminal.

## SYNTAX

### CredentialSet (Default)

```
Get-CredUICredential [[-Credential] <PSCredential>] [<CommonParameters>]
```

### MessageSet

```
Get-CredUICredential [[-UserName] <String>] [-Message <String>] [-Title <String>]
 [-ShowSaveCheckbox] [<CommonParameters>]
```

## ALIASES

## DESCRIPTION

The `Get-CredUICredential` cmdlet creates a credential object for a specified user name and password. You can use
the credential object in security operations.

`Get-CredUICredential` always prompts with the modern (Vista+) Windows credential dialog: the same dialog Windows
itself shows for UAC elevation and RDP logins. Unlike `Get-Credential`, it never falls back to a terminal prompt, and
there is no registry entry that changes this.

This cmdlet aims to be a drop-in alternative to `Get-Credential`. Its parameters and its output are the same, with
one addition: `-ShowSaveCheckbox`. Refer to the `Get-Credential` documentation for advanced usages.

This module is a maintained fork of the archived [Get-WinCredential](https://github.com/zbalkan/Get-WinCredential) by
Zafer Balkan. The code is based on the [Credential Management API examples by Alan Dean](https://www.developerfusion.com/code/4693/using-the-credential-management-api/).

The module is a binary module built for .NET 10, so it needs a PowerShell host running on .NET 10. That means
**PowerShell 7.6 or later**, on Windows.

## EXAMPLES

### Example 1

Get-CredUICredential
Gets a credential object and saves it in the `$creds` variable.

### Example 2

Get-CredUICredential -Message "Type your credentials"
Shows the dialog with a custom message.

### Example 3

Get-CredUICredential -Title "Creds" -Message "Type your credentials"
Shows the dialog with a custom window caption as well. The caption is useful with password managers such as KeePass
that match a window title to decide which stored credentials to auto-type.

### Example 4

Get-CredUICredential -Credential $creds
Returns `$creds` unchanged, without showing a dialog. This is what makes the cmdlet usable behind a
`[Parameter()][PSCredential]$Credential` of your own: pass it through, and the user is only prompted when they did
not supply one.
Note that, exactly as with `Get-Credential`, the `Credential` parameter does not accept pipeline input.
`$creds | Get-CredUICredential` does not bind.

### Example 5

$result = Get-CredUICredential -ShowSaveCheckbox
$result.Credential
$result.Checkbox
Shows the dialog with a Save check box. Since there are now two things to report, the cmdlet returns an object with
`Credential` and `Checkbox` properties instead of a bare `PSCredential`. The checkbox's label cannot be customized,
and checking it does not save anything by itself; persisting the credential is left to the caller.

## PARAMETERS

### -Credential

Specifies a PSCredential object. If a value is supplied, it is returned unchanged and no dialog is shown.

```yaml
Type: System.Management.Automation.PSCredential
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: CredentialSet
  Position: 0
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Message

Specifies the message that is displayed in the dialog box that prompts the user for credentials. Describe which
script or function is requesting the credential.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: MessageSet
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -ShowSaveCheckbox

Adds the native Save check box to the dialog box. Windows does not allow this checkbox's label to be customized,
and checking it does not save anything by itself. When this switch is used, the cmdlet returns an object with
Credential and Checkbox properties instead of a bare PSCredential.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: MessageSet
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Title

Specifies the caption of the dialog box that prompts the user for credentials. This can be helpful with password
management tools, such as KeePass, that match a window title to a password.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: MessageSet
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -UserName

Specifies the user name to pre-populate in the dialog's user name field. The user can still edit it or type a
different name; this only sets the initial value.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: MessageSet
  Position: 0
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

You cannot pipe input to this cmdlet.

## OUTPUTS

### System.Management.Automation.PSCredential

A credential object containing the user name and password entered in the dialog box, or the Credential object that
was passed in. Returned unless ShowSaveCheckbox is used.

### System.Management.Automation.PSObject

Returned only when ShowSaveCheckbox is used. Has a Credential property (the PSCredential entered in the dialog box)
and a Checkbox property (a boolean indicating whether the Save check box was checked).

## NOTES

This module is a maintained fork of the archived Get-WinCredential module by Zafer Balkan.

## RELATED LINKS

{{ Fill in the related links here }}

