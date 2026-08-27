using System.ComponentModel;
using System.Windows.Forms;
using CredUICredential.Pinvoke;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     What the dialog does when Windows says no. Whatever went wrong, the Win32 error has to
    ///     survive the trip out: it is the only thing that tells the caller which failure they are
    ///     looking at.
    /// </summary>
    public class CredentialsDialogFailureTests
    {
        [Fact]
        public void CancellingThePromptIsNotAFailure()
        {
            var dialog = new CredentialsDialog(
                new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_CANCELLED });

            Assert.Equal(DialogResult.Cancel, dialog.Show());
        }

        [Fact]
        public void CancellingThePromptLeavesNoCredentialBehind()
        {
            var dialog = new CredentialsDialog(
                new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_CANCELLED });

            dialog.Show();

            Assert.Null(dialog.Password);
        }

        [Theory]
        [InlineData(CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER)]
        [InlineData(CREDUI.ReturnCodes.ERROR_INVALID_FLAGS)]
        [InlineData(CREDUI.ReturnCodes.ERROR_NO_SUCH_LOGON_SESSION)]
        [InlineData(CREDUI.ReturnCodes.ERROR_NOT_FOUND)]
        [InlineData(CREDUI.ReturnCodes.ERROR_INVALID_ACCOUNT_NAME)]
        internal void AFailedPromptReportsTheWindowsErrorCode(CREDUI.ReturnCodes code)
        {
            var dialog = new CredentialsDialog(new ScriptedCredUi { PromptResult = code });

            var exception = Assert.Throws<Win32Exception>(() => dialog.Show());

            Assert.Equal((int)code, exception.NativeErrorCode);
        }

        [Fact]
        public void AnErrorTheModuleHasNeverHeardOfIsStillReportedByCode()
        {
            // ERROR_ACCESS_DENIED is not in the module's list, and a caller staring at
            // "Unknown credential result encountered" has nothing to go on.
            const int ERROR_ACCESS_DENIED = 5;
            var dialog = new CredentialsDialog(
                new ScriptedCredUi { PromptResult = (CREDUI.ReturnCodes)ERROR_ACCESS_DENIED });

            var exception = Assert.Throws<Win32Exception>(() => dialog.Show());

            Assert.Equal(ERROR_ACCESS_DENIED, exception.NativeErrorCode);
        }

        [Fact]
        public void ACredentialThatCannotBeDecodedIsAFailureRatherThanAnEmptySuccess()
        {
            // Reporting success here hands the caller a null password, which only blows up later
            // in PSCredential - a long way from the call that actually failed.
            var dialog = new CredentialsDialog(
                new ScriptedCredUi { UnpackFailsWith = (int)CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER });

            var exception = Assert.Throws<Win32Exception>(() => dialog.Show());

            Assert.Equal((int)CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER, exception.NativeErrorCode);
        }

        [Fact]
        public void ADecodeThatNeverFitsIsReportedRatherThanRetriedForever()
        {
            var api = new ScriptedCredUi
            {
                UnpackFailsWith = (int)CREDUI.ReturnCodes.ERROR_INSUFFICIENT_BUFFER,
            };

            Assert.Throws<Win32Exception>(() => new CredentialsDialog(api).Show());
            Assert.Equal(2, api.UnpackAttempts.Count);
        }
    }
}
