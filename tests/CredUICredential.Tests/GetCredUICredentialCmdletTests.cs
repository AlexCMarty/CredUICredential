using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Security;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Covers the paths through <see cref="GetCredUICredentialCmdlet"/> that must not raise the
    ///     modal Windows credential dialog: the pass-through of an already-supplied credential, and
    ///     the validation that rejects bad input during parameter binding.
    /// </summary>
    public class GetCredUICredentialCmdletTests : IDisposable
    {
        private readonly PowerShellHost _host = new();

        public void Dispose() => _host.Dispose();

        internal static PSCredential MakeCredential(string userName = "alice", string password = "s3cret")
        {
            var secure = new SecureString();
            foreach (var c in password)
            {
                secure.AppendChar(c);
            }
            secure.MakeReadOnly();
            return new PSCredential(userName, secure);
        }

        private static string Reveal(PSCredential credential)
            => credential.GetNetworkCredential().Password;

        [Fact]
        public void SuppliedCredentialIsPassedStraightThroughWithoutPrompting()
        {
            var credential = MakeCredential();

            var output = _host.Run(
                "Get-CredUICredential -Credential $given",
                new Dictionary<string, object> { ["given"] = credential });

            var returned = Assert.IsType<PSCredential>(Assert.Single(output).BaseObject);
            Assert.Same(credential, returned);
        }

        [Fact]
        public void SuppliedCredentialBindsPositionally()
        {
            var credential = MakeCredential("CONTOSO\bob", "hunter2");

            var output = _host.Run(
                "Get-CredUICredential $given",
                new Dictionary<string, object> { ["given"] = credential });

            var returned = Assert.IsType<PSCredential>(Assert.Single(output).BaseObject);
            Assert.Equal("CONTOSO\bob", returned.UserName);
            Assert.Equal("hunter2", Reveal(returned));
        }

        [Fact]
        public void SuppliedCredentialWritesNothingToTheErrorStream()
        {
            var credential = MakeCredential();

            _host.Run(
                "Get-CredUICredential -Credential $given",
                out var errors,
                new Dictionary<string, object> { ["given"] = credential });

            Assert.Empty(errors);
        }

        [Fact]
        public void NullCredentialIsRejectedRatherThanFallingBackToTheDialog()
        {
            var output = _host.Run("Get-CredUICredential -Credential $null", out var errors);

            Assert.Empty(output);
            var error = Assert.Single(errors);
            Assert.Contains("Credential", error.Exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CredentialCannotBeCombinedWithTheDialogOnlyParameters()
        {
            var credential = MakeCredential();

            var output = _host.Run(
                "Get-CredUICredential -Credential $given -Title 'nope'",
                out var errors,
                new Dictionary<string, object> { ["given"] = credential });

            Assert.Empty(output);
            Assert.Single(errors);
        }

        [Fact]
        public void EmptyTitleIsRejectedBeforeTheDialogIsRaised()
        {
            var output = _host.Run("Get-CredUICredential -Title ''", out var errors);

            Assert.Empty(output);
            Assert.Single(errors);
        }

        [Fact]
        public void EmptyMessageIsRejectedBeforeTheDialogIsRaised()
        {
            var output = _host.Run("Get-CredUICredential -Message ''", out var errors);

            Assert.Empty(output);
            Assert.Single(errors);
        }

        [Fact]
        public void EmptyUserNameIsRejectedBeforeTheDialogIsRaised()
        {
            var output = _host.Run("Get-CredUICredential -UserName ''", out var errors);

            Assert.Empty(output);
            Assert.Single(errors);
        }
    }
}
