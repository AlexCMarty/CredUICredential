using System.Management.Automation;
using System.Management.Automation.Runspaces;
using CredUICredential.Tests.Fakes;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     <see cref="GetCredUICredentialCmdlet"/> with a scripted <c>credui.dll</c> underneath it.
    /// </summary>
    /// <remarks>
    ///     Only the dialog is substituted. The cmdlet is still registered in a real runspace and
    ///     still goes through real parameter binding, real output streams and real error records,
    ///     so what the tests observe is what a user would see.
    /// </remarks>
    internal sealed class ScriptedDialogCmdlet : GetCredUICredentialCmdlet
    {
        /// <summary>
        ///     The stand-in the next invocation will prompt through. PowerShell constructs the
        ///     cmdlet itself, so there is nowhere else to hand it in.
        /// </summary>
        internal static ScriptedCredUi Api { get; set; }

        internal override CredentialsDialog CreateDialog()
            => new(Api, caption: Title, message: Message);
    }

    /// <summary>
    ///     A runspace hosting <see cref="ScriptedDialogCmdlet"/> under the cmdlet's real name.
    /// </summary>
    internal sealed class ScriptedDialogHost : System.IDisposable
    {
        private readonly Runspace _runspace;

        public ScriptedDialogHost(ScriptedCredUi api)
        {
            ScriptedDialogCmdlet.Api = api;

            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry(
                "Get-CredUICredential", typeof(ScriptedDialogCmdlet), null));

            _runspace = RunspaceFactory.CreateRunspace(state);
            _runspace.Open();
        }

        public System.Collections.ObjectModel.Collection<PSObject> Run(
            string script,
            out System.Collections.Generic.IReadOnlyList<ErrorRecord> errors)
        {
            System.Collections.Generic.IReadOnlyList<ErrorRecord> captured = null;
            var output = NoDialog.Expected(() =>
            {
                using var shell = PowerShell.Create();
                shell.Runspace = _runspace;
                var result = shell.AddScript(script).Invoke();
                captured = new System.Collections.Generic.List<ErrorRecord>(shell.Streams.Error);
                return result;
            });

            errors = captured;
            return output;
        }

        public void Dispose()
        {
            _runspace.Dispose();
            ScriptedDialogCmdlet.Api = null;
        }
    }
}
