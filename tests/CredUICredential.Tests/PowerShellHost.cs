using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Runs scripts in a runspace that has <c>Get-CredUICredential</c> registered, so the
    ///     cmdlet is exercised through real PowerShell command discovery, parameter binding and
    ///     the pipeline rather than by calling its methods directly.
    /// </summary>
    /// <remarks>
    ///     Every invocation is guarded by <see cref="NoDialog"/>. None of the scripts the tests
    ///     run should reach the modal credential dialog, and a regression that made one of them
    ///     prompt would otherwise hang the run instead of failing it.
    /// </remarks>
    internal sealed class PowerShellHost : IDisposable
    {
        private readonly Runspace _runspace;

        public PowerShellHost()
        {
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry(
                "Get-CredUICredential", typeof(GetCredUICredentialCmdlet), null));

            _runspace = RunspaceFactory.CreateRunspace(state);
            _runspace.Open();
        }

        /// <summary>Runs <paramref name="script"/> and returns everything it emitted.</summary>
        /// <param name="errors">Anything written to the error stream.</param>
        public Collection<PSObject> Run(
            string script,
            out IReadOnlyList<ErrorRecord> errors,
            IDictionary<string, object> variables = null)
        {
            if (variables != null)
            {
                foreach (var pair in variables)
                {
                    _runspace.SessionStateProxy.SetVariable(pair.Key, pair.Value);
                }
            }

            IReadOnlyList<ErrorRecord> captured = null;
            var output = NoDialog.Expected(() =>
            {
                using var shell = PowerShell.Create();
                shell.Runspace = _runspace;
                var result = shell.AddScript(script).Invoke();
                captured = new List<ErrorRecord>(shell.Streams.Error);
                return result;
            });

            errors = captured;
            return output;
        }

        public Collection<PSObject> Run(string script, IDictionary<string, object> variables = null)
            => Run(script, out _, variables);

        public void Dispose() => _runspace.Dispose();
    }
}
