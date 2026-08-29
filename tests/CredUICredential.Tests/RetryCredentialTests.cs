using System;
using System.ComponentModel;
using System.Management.Automation;
using CredUICredential.Pinvoke;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Retrying the prompt until the password logs on, or until it logs on as a local
    ///     administrator. The dialog is scripted; so is <c>LogonUser</c>.
    /// </summary>
    [Collection("ScriptedDialog")]
    public class RetryCredentialTests
    {
        private static string Reveal(PSCredential credential)
            => credential.GetNetworkCredential().Password;

        [Fact]
        public void RetryNormalUserReturnsTheCredentialOnTheFirstValidLogon()
        {
            var api = new ScriptedCredUi { UserName = "alice", Password = "hunter2" };
            var logon = new ScriptedLogon(LogonResult.Succeeded(isLocalAdministrator: false));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser", out var errors);

            Assert.Empty(errors);
            var credential = Assert.IsType<PSCredential>(Assert.Single(output).BaseObject);
            Assert.Equal("alice", credential.UserName);
            Assert.Equal("hunter2", Reveal(credential));
            Assert.Equal(1, api.PromptCount);
            Assert.Equal(new[] { 0 }, api.RequestedAuthErrors);
        }

        [Fact]
        public void RetryNormalUserShowsTheNativeLogonFailureAndAsksAgain()
        {
            var api = new ScriptedCredUi
            {
                UserName = "alice",
                PasswordsByAttempt = { "wrong", "hunter2" }
            };
            var logon = new ScriptedLogon(
                LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE),
                LogonResult.Succeeded(isLocalAdministrator: false));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser", out var errors);

            Assert.Empty(errors);
            Assert.Equal("hunter2", Reveal(Assert.IsType<PSCredential>(Assert.Single(output).BaseObject)));
            Assert.Equal(2, api.PromptCount);
            Assert.Equal(new[] { 0, ADVAPI.ERROR_LOGON_FAILURE }, api.RequestedAuthErrors);
        }

        [Fact]
        public void RetryNormalUserWritesAnErrorAfterTheDefaultThreeFailures()
        {
            var api = new ScriptedCredUi();
            var logon = new ScriptedLogon(
                LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE),
                LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE),
                LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser", out var errors);

            Assert.Empty(output);
            Assert.Equal(3, api.PromptCount);
            var error = Assert.Single(errors);
            Assert.Contains("CredentialValidationFailed", error.FullyQualifiedErrorId);
            Assert.Equal(ErrorCategory.AuthenticationError, error.CategoryInfo.Category);
            Assert.Equal(ADVAPI.ERROR_LOGON_FAILURE, Assert.IsType<Win32Exception>(error.Exception).NativeErrorCode);
            Assert.Equal("alice", error.TargetObject);
        }

        [Fact]
        public void MaxAttemptsOneStopsAfterASingleFailure()
        {
            var api = new ScriptedCredUi();
            var logon = new ScriptedLogon(LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser -MaxAttempts 1", out var errors);

            Assert.Empty(output);
            Assert.Equal(1, api.PromptCount);
            Assert.Contains("CredentialValidationFailed", Assert.Single(errors).FullyQualifiedErrorId);
        }

        [Fact]
        public void CancellingTheFirstPromptIsSilent()
        {
            var api = new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_CANCELLED };
            var logon = new ScriptedLogon();
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser", out var errors);

            Assert.Empty(output);
            Assert.Empty(errors);
            Assert.Empty(logon.Calls);
        }

        [Fact]
        public void CancellingAfterAFailedAttemptIsSilent()
        {
            var api = new ScriptedCredUi
            {
                PromptResultsByAttempt =
                {
                    CREDUI.ReturnCodes.NO_ERROR,
                    CREDUI.ReturnCodes.ERROR_CANCELLED
                }
            };
            var logon = new ScriptedLogon(LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser", out var errors);

            Assert.Empty(output);
            Assert.Empty(errors);
            Assert.Equal(2, api.PromptCount);
            Assert.Single(logon.Calls);
        }

        [Fact]
        public void ANonRetryableLogonErrorStopsWithoutAskingAgain()
        {
            var api = new ScriptedCredUi { UserName = "alice" };
            var logon = new ScriptedLogon(LogonResult.Failed(ADVAPI.ERROR_ACCOUNT_LOCKED_OUT));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser", out var errors);

            Assert.Empty(output);
            Assert.Equal(1, api.PromptCount);
            var error = Assert.Single(errors);
            Assert.Contains("CredentialLogonFailed", error.FullyQualifiedErrorId);
            Assert.Equal(ErrorCategory.AuthenticationError, error.CategoryInfo.Category);
            Assert.Equal(ADVAPI.ERROR_ACCOUNT_LOCKED_OUT, Assert.IsType<Win32Exception>(error.Exception).NativeErrorCode);
            Assert.Equal("alice", error.TargetObject);
        }

        [Fact]
        public void RetryAdminUserAcceptsALocalAdministrator()
        {
            var api = new ScriptedCredUi { UserName = "admin", Password = "hunter2" };
            var logon = new ScriptedLogon(LogonResult.Succeeded(isLocalAdministrator: true));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryAdminUser", out var errors);

            Assert.Empty(errors);
            Assert.Equal("admin", Assert.IsType<PSCredential>(Assert.Single(output).BaseObject).UserName);
            Assert.Equal(1, api.PromptCount);
        }

        [Fact]
        public void RetryAdminUserAsksAgainWhenTheAccountIsNotAnAdministrator()
        {
            var api = new ScriptedCredUi
            {
                UserNamesByAttempt = { "bob", "admin" },
                PasswordsByAttempt = { "pw", "hunter2" }
            };
            var logon = new ScriptedLogon(
                LogonResult.Succeeded(isLocalAdministrator: false),
                LogonResult.Succeeded(isLocalAdministrator: true));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryAdminUser", out var errors);

            Assert.Empty(errors);
            Assert.Equal("admin", Assert.IsType<PSCredential>(Assert.Single(output).BaseObject).UserName);
            Assert.Equal(2, api.PromptCount);
            Assert.Equal(new[] { 0, ADVAPI.ERROR_ELEVATION_REQUIRED }, api.RequestedAuthErrors);
        }

        [Fact]
        public void RetryAdminUserWritesAnErrorAfterExhaustingNonAdminAttempts()
        {
            var api = new ScriptedCredUi { UserName = "bob" };
            var logon = new ScriptedLogon(
                LogonResult.Succeeded(isLocalAdministrator: false),
                LogonResult.Succeeded(isLocalAdministrator: false));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryAdminUser -MaxAttempts 2", out var errors);

            Assert.Empty(output);
            Assert.Equal(2, api.PromptCount);
            var error = Assert.Single(errors);
            Assert.Contains("CredentialNotAdministrator", error.FullyQualifiedErrorId);
            Assert.Equal(ErrorCategory.PermissionDenied, error.CategoryInfo.Category);
            Assert.Equal("bob", error.TargetObject);
        }

        [Fact]
        public void RetryAdminUserTreatsAWrongPasswordLikeRetryNormalUser()
        {
            var api = new ScriptedCredUi { PasswordsByAttempt = { "wrong", "hunter2" } };
            var logon = new ScriptedLogon(
                LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE),
                LogonResult.Succeeded(isLocalAdministrator: true));
            using var host = new ScriptedDialogHost(api, logon);

            host.Run("Get-CredUICredential -RetryAdminUser", out var errors);

            Assert.Empty(errors);
            Assert.Equal(new[] { 0, ADVAPI.ERROR_LOGON_FAILURE }, api.RequestedAuthErrors);
        }

        [Fact]
        public void AFailedAttemptReseedsTheUserNameOnTheNextPrompt()
        {
            var api = new ScriptedCredUi
            {
                UserNamesByAttempt = { "typed-first", "typed-second" },
                PasswordsByAttempt = { "wrong", "hunter2" }
            };
            var logon = new ScriptedLogon(
                LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE),
                LogonResult.Succeeded(isLocalAdministrator: false));
            using var host = new ScriptedDialogHost(api, logon);

            host.Run("Get-CredUICredential -RetryNormalUser -UserName 'seeded'", out _);

            Assert.Equal(new[] { "seeded", "typed-first" }, api.PackedUserNames);
        }

        [Fact]
        public void ShowSaveCheckboxReportsTheBoxFromTheSuccessfulAttempt()
        {
            var api = new ScriptedCredUi
            {
                PasswordsByAttempt = { "wrong", "hunter2" },
                SaveCheckedByAttempt = { false, true }
            };
            var logon = new ScriptedLogon(
                LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE),
                LogonResult.Succeeded(isLocalAdministrator: false));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser -ShowSaveCheckbox", out var errors);

            Assert.Empty(errors);
            Assert.True((bool)Assert.Single(output).Properties["Checkbox"].Value);
        }

        [Fact]
        public void WithoutARetrySwitchTheLogonApiIsNeverCreated()
        {
            var api = new ScriptedCredUi();
            using var host = new ScriptedDialogHost(api, new ScriptedLogon());

            host.Run("Get-CredUICredential -Message 'go on then'", out var errors);

            Assert.Empty(errors);
            Assert.Equal(0, ScriptedDialogCmdlet.LogonApiCreations);
        }

        [Fact]
        public void BothRetrySwitchesCannotBeCombined()
        {
            using var host = new ScriptedDialogHost(new ScriptedCredUi(), new ScriptedLogon());

            var output = host.Run(
                "Get-CredUICredential -RetryNormalUser -RetryAdminUser",
                out var errors);

            Assert.Empty(output);
            Assert.Single(errors);
            Assert.Equal(0, ScriptedDialogCmdlet.LogonApiCreations);
        }

        [Fact]
        public void MaxAttemptsWithoutARetrySwitchDoesNotBind()
        {
            using var host = new ScriptedDialogHost(new ScriptedCredUi());

            var output = host.Run("Get-CredUICredential -MaxAttempts 3", out var errors);

            Assert.Empty(output);
            Assert.Single(errors);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(11)]
        public void MaxAttemptsOutsideTheAllowedRangeIsRejected(int attempts)
        {
            using var host = new ScriptedDialogHost(new ScriptedCredUi(), new ScriptedLogon());

            var output = host.Run(
                $"Get-CredUICredential -RetryNormalUser -MaxAttempts {attempts}",
                out var errors);

            Assert.Empty(output);
            Assert.Single(errors);
            Assert.Equal(0, ScriptedDialogCmdlet.LogonApiCreations);
        }

        [Fact]
        public void RetryNormalUserTreatsANonPasswordCredentialLikeAFailedAttempt()
        {
            var api = new ScriptedCredUi
            {
                UserName = "alice",
                PasswordsByAttempt = { "garbage", "hunter2" },
                MessageTypesByAttempt = { KERB.SmartCardLogon, KERB.InteractiveLogon }
            };
            var logon = new ScriptedLogon(
                LogonResult.Succeeded(isLocalAdministrator: false));
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser", out var errors);

            Assert.Empty(errors);
            Assert.Equal("hunter2", Reveal(Assert.IsType<PSCredential>(Assert.Single(output).BaseObject)));
            Assert.Equal(2, api.PromptCount);
            Assert.Equal(new[] { 0, ADVAPI.ERROR_LOGON_FAILURE }, api.RequestedAuthErrors);
            Assert.Equal(1, logon.AttemptCount);
        }

        [Fact]
        public void RetryNormalUserWritesAnErrorWhenEveryAttemptIsNonPassword()
        {
            var api = new ScriptedCredUi { MessageType = KERB.SmartCardLogon };
            var logon = new ScriptedLogon();
            using var host = new ScriptedDialogHost(api, logon);

            var output = host.Run("Get-CredUICredential -RetryNormalUser -MaxAttempts 2", out var errors);

            Assert.Empty(output);
            Assert.Equal(2, api.PromptCount);
            Assert.Equal(0, logon.AttemptCount);
            var error = Assert.Single(errors);
            Assert.Contains("CredentialNotPassword", error.FullyQualifiedErrorId);
        }
    }
}
