## Commits

You MUST use conventional commits.

```
feat|bugfix|chore|etc: short

<longer text>
```

You MUST break your work into logical commits. Do not pile unrelated concerns into a single commit.

## What this is

A PowerShell binary module that exports one cmdlet, `Get-CredUICredential`. It P/Invokes
`CredUIPromptForWindowsCredentials` in `credui.dll` to raise the modern (Vista+) Windows credential
dialog, so scripts get a real Windows prompt instead of `Get-Credential`'s terminal prompt.

It is a maintained fork of the archived [Get-WinCredential](https://github.com/zbalkan/Get-WinCredential).

`Get-CredUICredential` is meant to be a **drop-in replacement** for the built-in `Get-Credential`:
same parameters, same parameter sets, same output, plus `-ShowSaveCheckbox`. `DropInParityTests`
enforces this by comparing the two command surfaces at runtime, so a change to `Credential`,
`Message`, `Title` or `UserName` binding that diverges from the built-in cmdlet fails the build.
If you deliberately want to diverge, that test is the thing to argue with.

## Commands

```bash
dotnet build
```

```bash
dotnet test tests/CredUICredential.Tests/CredUICredential.Tests.csproj
```

One test class, or one test:

```bash
dotnet test tests/CredUICredential.Tests/CredUICredential.Tests.csproj --filter 'FullyQualifiedName~CredentialsDialogTests'
```

Note that an imported module locks `CredUICredential/bin/Release/**/CredUICredential.dll`, 
so a Release build will fail to overwrite it until that PowerShell session exits.

## Layout

| Path | What lives there |
| --- | --- |
| `CredUICredential/GetCredUICredentialCmdlet.cs` | The cmdlet: parameters, output shape, error records |
| `CredUICredential/CredentialsDialog.cs` | Everything around the native prompt: flags, buffers, decoding, cleanup |
| `CredUICredential/Plaintext.cs` | The window in which the password exists as characters |
| `CredUICredential/Pinvoke/` | The `credui.dll` declarations and the `ICredUiApi` seam over them |
| `CredUICredential.psd1` | Module manifest — what the Gallery publishes |
| `en-US/CredUICredential.dll-Help.xml` | Hand-written MAML; this is what `Get-Help` prints |
| `CredUICredential.md` | The documentation `HelpUri` points at |
| `tests/CredUICredential.Tests/` | xunit suite |

## Testing a modal dialog

The whole point of the module is a blocking, interactive Windows dialog, which no test can dismiss.
Two mechanisms make it testable, and new tests should reach for them in this order:

**`RealBufferCredUi`** (`tests/.../Fakes/`) replaces *only* the prompt. It packs the requested user
name and password with the real `CredPackAuthenticationBuffer` — the non-interactive counterpart of
the dialog, which produces the identical buffer format — and delegates decoding and freeing to the
real `credui.dll`. Prefer this: the code under test is the shipping path, marshalling and all.

**`ScriptedCredUi`** replaces the whole native layer, for outcomes Windows cannot be asked for on
demand: a cancelled prompt, a prompt that fails with a specific Win32 error, a buffer that will not
decode, a domain reported separately from the user name. It implements the documented
`ERROR_INSUFFICIENT_BUFFER` protocol faithfully — refusing to write into a buffer that is too small
and reporting the size it needs — so do not "simplify" that away.

Both are injected through `internal CredentialsDialog(ICredUiApi api, ...)`. For cmdlet-level tests,
`ScriptedDialogCmdlet` overrides `GetCredUICredentialCmdlet.CreateDialog()` and is registered in a
real runspace under the cmdlet's real name, so parameter binding, output streams and error records
are all genuine.

`InternalsVisibleTo` in `CredUICredential.csproj` is what lets tests see any of this.

### Anything that runs a script in a runspace goes through `NoDialog`

`PowerShellHost.Run` and `ScriptedDialogHost.Run` wrap every invocation in `NoDialog.Expected`. A
regression that starts prompting on a path that should not would otherwise hang the entire test run
on a dialog nobody is there to dismiss. Keep new runspace helpers behind it.

## Things that will bite you

**The native buffer contract.** `CredUnPackAuthenticationBuffer` does not truncate. Handed a buffer
that is too small it writes nothing, fails with `ERROR_INSUFFICIENT_BUFFER`, and reports the sizes
it needs through the capacity arguments. Ignoring that return value is how this module used to lose
any password over 100 characters. The constants in `Pinvoke/CREDUI.cs` are Windows' published
ceilings from `wincred.h`, not sizes to trust blindly — the retry in `TryReadCredential` is what
makes it correct.

**The authentication buffer is ours and it holds the password in the clear.** It is released from a
`finally` and zeroed on the way out. Do not move that release onto a success path.

**Win32 calling convention.** The `credui.dll` imports must be `Winapi`. They were `Cdecl` for a
long time, which is harmless on x64 (one calling convention) and corrupts the stack on the x86 build
of PowerShell. No test on an x64 machine can catch this.

**The manifest and the assembly do not check each other.** `ModuleVersion` in `CredUICredential.psd1`
and `<Version>` in `CredUICredential.csproj` are maintained by hand; `ModuleManifestTests` fails if
they drift. `PowerShellVersion` is likewise tied to the target framework — a binary module built for
.NET 10 needs PowerShell 7.6 or later, and understating that just turns a clear error into an
assembly load failure.

**The MAML help is hand-written.** Nothing regenerates it. `HelpDocumentationTests` compares it to
the cmdlet type, so adding or renaming a parameter fails the build until `en-US/CredUICredential.dll-Help.xml`
is updated too.

## Known limitation

`-UserName` is accepted for `Get-Credential` compatibility but has no effect: the modern CredUI
dialog offers no way to pre-populate the user name field. Seeding it would mean building an input
buffer with `CredPackAuthenticationBuffer` and passing it to `CredUIPromptForWindowsCredentials` —
possible, but not implemented. The README, `CredUICredential.md` and the MAML help all say so; keep
them saying so unless it actually gets built.
