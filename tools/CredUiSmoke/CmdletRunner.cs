using System.Diagnostics;
using System.IO;
using System.Text;

namespace CredUiSmoke;

/// <summary>
///     Runs the real cmdlet, in a real PowerShell, with whatever parameters the caller asks for.
///     <para>
///         The harness's own <c>enumerate</c> / <c>shot</c> commands call
///         <c>CredUIPromptForWindowsCredentials</c> directly, so they can raise any dialog the API
///         can raise - including combinations of flags the module never passes. That makes them
///         good for exploring credui and bad for evidence: a picture taken that way says what
///         Windows can draw, not what <c>Get-CredUICredential</c> draws. This runs the shipping
///         path instead, so a capture is of the dialog a script would actually get.
///     </para>
///     <para>
///         The child never prints the credential. It reports the shape of what came back - the
///         output type, the user name, the LENGTH of the password - the same contract as
///         <c>Invoke-ModuleSmoke.ps1</c>.
///     </para>
/// </summary>
internal sealed class CmdletRunner
{
    /// <summary>
    ///     What the dialog says when <c>-Message</c> is not passed. Hardcoded in
    ///     <c>CredentialsDialog</c>'s constructor, and the only thing that identifies a bare call's
    ///     window on the desktop.
    /// </summary>
    internal const string DefaultMessage = "Enter your credentials.";

    private readonly string _reportPath =
        Path.Combine(Path.GetTempPath(), $"credui-smoke-cmdlet-{Environment.ProcessId}.json");

    private Process? _process;

    internal required string ManifestPath { get; init; }

    /// <summary>Cmdlet parameters, passed through verbatim to the child's command line.</summary>
    internal string Arguments { get; init; } = string.Empty;

    internal int ProcessId => _process?.Id ?? 0;

    internal void Start()
    {
        File.Delete(_reportPath);

        // Two dollars, so a single brace is literal and {{these}} are the holes: the script is
        // mostly PowerShell braces, and escaping every one of them would be unreadable.
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                Import-Module '{{Quote(ManifestPath)}}' -Force
            } catch {
                [ordered]@{ importError = $_.Exception.Message } | ConvertTo-Json |
                    Set-Content -LiteralPath '{{Quote(_reportPath)}}' -Encoding UTF8
                exit 1
            }

            $ErrorActionPreference = 'Continue'
            $errors = @()
            $result = Get-CredUICredential {{Arguments}} -ErrorVariable errors -ErrorAction SilentlyContinue
            $credential = if ($result -is [System.Management.Automation.PSCredential]) { $result }
                          elseif ($result -and $result.PSObject.Properties['Credential']) { $result.Credential }
                          else { $null }
            [ordered]@{
                outputType     = if ($result) { $result.GetType().FullName } else { '<none>' }
                hasCredential  = [bool]$credential
                userName       = if ($credential) { $credential.UserName } else { '' }
                passwordLength = if ($credential) { $credential.GetNetworkCredential().Password.Length } else { -1 }
                checkbox       = if ($result -and $result.PSObject.Properties['Checkbox']) { $result.Checkbox } else { $null }
                errorIds       = @($errors | ForEach-Object { $_.FullyQualifiedErrorId })
                errorMessages  = @($errors | ForEach-Object { $_.Exception.Message })
            } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath '{{Quote(_reportPath)}}' -Encoding UTF8
            """;

        // -EncodedCommand takes UTF-16LE base64, which sidesteps every quoting question the
        // script could otherwise raise on the way through a command line.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        _process = Process.Start(new ProcessStartInfo("pwsh")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded },
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    internal bool Wait(TimeSpan timeout) => _process?.WaitForExit((int)timeout.TotalMilliseconds) ?? true;

    /// <summary>
    ///     Kills the child if it is still sitting on a dialog. Not tidiness: an abandoned modal
    ///     prompt belongs to nobody and has to be dismissed by hand.
    /// </summary>
    internal void Kill()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or SystemException)
        {
        }
    }

    internal string ReadReport()
    {
        try
        {
            return File.Exists(_reportPath)
                ? File.ReadAllText(_reportPath)
                : "The child produced no report, so the cmdlet never returned.";
        }
        catch (IOException exception)
        {
            return $"Could not read the child's report: {exception.Message}";
        }
        finally
        {
            try
            {
                File.Delete(_reportPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Quote(string value) => value.Replace("'", "''");
}
