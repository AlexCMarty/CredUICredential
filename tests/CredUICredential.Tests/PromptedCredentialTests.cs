using System;
using System.ComponentModel;
using System.Management.Automation;
using CredUICredential.Pinvoke;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     What <c>Get-CredUICredential</c> emits once the dialog has been dealt with: the shape of
    ///     the output, and what reaches the error stream when the prompt goes wrong.
    /// </summary>
    /// <remarks>
    ///     The cmdlet runs in a real runspace with a scripted <c>credui.dll</c> underneath, so
    ///     these assertions are about the objects a user would actually get back.
    /// </remarks>
    [Collection("ScriptedDialog")]
    public class PromptedCredentialTests
    {
        private static string Reveal(PSCredential credential)
            => credential.GetNetworkCredential().Password;

        [Fact]
        public void ThePromptedCredentialIsReturned()
        {
            using var host = new ScriptedDialogHost(
                new ScriptedCredUi { UserName = "CONTOSO\\alice", Password = "hunter2" });

            var output = host.Run("Get-CredUICredential -Message 'go on then'", out var errors);

            Assert.Empty(errors);
            var credential = Assert.IsType<PSCredential>(Assert.Single(output).BaseObject);
            Assert.Equal("CONTOSO\\alice", credential.UserName);
            Assert.Equal("hunter2", Reveal(credential));
        }

        [Fact]
        public void WithoutTheSwitchTheOutputIsABarePSCredential()
        {
            using var host = new ScriptedDialogHost(new ScriptedCredUi());

            var output = host.Run("Get-CredUICredential -Message 'go on then'", out _);

            var emitted = Assert.Single(output);
            Assert.IsType<PSCredential>(emitted.BaseObject);
            Assert.DoesNotContain(
                emitted.Properties,
                property => property.Name.Equals("Checkbox", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void CancellingTheDialogProducesNothingAtAll()
        {
            using var host = new ScriptedDialogHost(
                new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_CANCELLED });

            var output = host.Run("Get-CredUICredential -Message 'go on then'", out var errors);

            Assert.Empty(output);
            Assert.Empty(errors);
        }

        [Fact]
        public void AFailedPromptBecomesAnErrorRecordRatherThanACrash()
        {
            using var host = new ScriptedDialogHost(
                new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER });

            var output = host.Run("Get-CredUICredential -Message 'go on then'", out var errors);

            Assert.Empty(output);
            var error = Assert.Single(errors);
            var win32 = Assert.IsType<Win32Exception>(error.Exception);
            Assert.Equal((int)CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER, win32.NativeErrorCode);
        }

        [Fact]
        public void AFailedPromptIsReportedAgainstTheCmdletsOwnErrorId()
        {
            using var host = new ScriptedDialogHost(
                new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER });

            host.Run("Get-CredUICredential -Message 'go on then'", out var errors);

            Assert.Contains("CouldNotPromptForCredential", Assert.Single(errors).FullyQualifiedErrorId);
        }

        [Fact]
        public void TheSaveCheckBoxIsReportedAlongsideTheCredential()
        {
            using var host = new ScriptedDialogHost(
                new ScriptedCredUi { UserName = "alice", Password = "pw", SaveChecked = true });

            var output = host.Run("Get-CredUICredential -ShowSaveCheckbox", out var errors);

            Assert.Empty(errors);
            var result = Assert.Single(output);
            Assert.True((bool)result.Properties["Checkbox"].Value);
            var credential = Assert.IsType<PSCredential>(result.Properties["Credential"].Value);
            Assert.Equal("alice", credential.UserName);
            Assert.Equal("pw", Reveal(credential));
        }

        [Fact]
        public void AnUncheckedSaveBoxIsReportedToo()
        {
            using var host = new ScriptedDialogHost(new ScriptedCredUi { SaveChecked = false });

            var output = host.Run("Get-CredUICredential -ShowSaveCheckbox", out _);

            Assert.False((bool)Assert.Single(output).Properties["Checkbox"].Value);
        }

        [Fact]
        public void CancellingADialogWithASaveBoxProducesNothingAtAll()
        {
            using var host = new ScriptedDialogHost(
                new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_CANCELLED });

            var output = host.Run("Get-CredUICredential -ShowSaveCheckbox", out var errors);

            Assert.Empty(output);
            Assert.Empty(errors);
        }

        [Fact]
        public void ANonPasswordCredentialBecomesAnErrorRecordRatherThanACredential()
        {
            // A smart-card submit through "More choices": credui packs it, and unpack even
            // succeeds, but what comes back is not a reusable password.
            var api = new ScriptedCredUi { MessageType = KERB.SmartCardLogon, UserName = "alice", Password = "garbage" };
            using var host = new ScriptedDialogHost(api);

            var output = host.Run("Get-CredUICredential -Message 'go on then'", out var errors);

            Assert.Empty(output);
            var error = Assert.Single(errors);
            Assert.Contains("CredentialNotPassword", error.FullyQualifiedErrorId);
            Assert.Equal(ErrorCategory.InvalidData, error.CategoryInfo.Category);
        }

        [Fact]
        public void AnInteractiveLogonStillReturnsTheCredential()
        {
            var api = new ScriptedCredUi { MessageType = KERB.InteractiveLogon, UserName = "alice", Password = "hunter2" };
            using var host = new ScriptedDialogHost(api);

            var output = host.Run("Get-CredUICredential -Message 'go on then'", out var errors);

            Assert.Empty(errors);
            Assert.Equal("hunter2", Reveal(Assert.IsType<PSCredential>(Assert.Single(output).BaseObject)));
        }

        [Fact]
        public void TitleAndMessageReachTheDialog()
        {
            var api = new ScriptedCredUi();
            using var host = new ScriptedDialogHost(api);

            host.Run("Get-CredUICredential -Title 'Admin needed' -Message 'Elevate to continue'", out _);

            var info = Assert.NotNull(api.RequestedInfo);
            Assert.Equal("Admin needed", info.pszCaptionText);
            Assert.Equal("Elevate to continue", info.pszMessageText);
        }
    }
}
