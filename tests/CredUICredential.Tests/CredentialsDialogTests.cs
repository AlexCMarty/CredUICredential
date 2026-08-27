using System;
using System.Net;
using System.Security;
using System.Windows.Forms;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Drives <see cref="CredentialsDialog"/> end to end with the interactive prompt replaced
    ///     by a buffer that Windows itself packed. Everything downstream of the prompt - decoding,
    ///     sizing, cleanup - is the real shipping code path.
    /// </summary>
    public class CredentialsDialogTests
    {
        private static string Reveal(SecureString secure)
            => new NetworkCredential(string.Empty, secure).Password;

        [Fact]
        public void DismissedDialogReportsOk()
        {
            var dialog = new CredentialsDialog(new RealBufferCredUi());

            Assert.Equal(DialogResult.OK, dialog.Show());
        }

        [Fact]
        public void TypedUserNameIsReadBackFromTheBuffer()
        {
            var dialog = new CredentialsDialog(new RealBufferCredUi { UserName = "CONTOSO\\alice" });

            dialog.Show();

            Assert.Equal("CONTOSO\\alice", dialog.UserName);
        }

        [Fact]
        public void TypedPasswordIsReadBackFromTheBuffer()
        {
            var dialog = new CredentialsDialog(new RealBufferCredUi { Password = "correct horse" });

            dialog.Show();

            Assert.Equal("correct horse", Reveal(dialog.Password));
        }

        [Fact]
        public void LongPasswordSurvivesTheRoundTrip()
        {
            // Windows lets a credential password run to 256 characters. Anything the dialog
            // accepts has to come back intact; silently returning a null password instead would
            // blow up in PSCredential, far away from the cause.
            var typed = new string('p', 200);
            var dialog = new CredentialsDialog(new RealBufferCredUi { Password = typed });

            dialog.Show();

            Assert.Equal(typed, Reveal(dialog.Password));
        }

        [Fact]
        public void LongUserNameSurvivesTheRoundTrip()
        {
            var typed = new string('u', 200);
            var dialog = new CredentialsDialog(new RealBufferCredUi { UserName = typed });

            dialog.Show();

            Assert.Equal(typed, dialog.UserName);
        }

        [Fact]
        public void AValueTooLargeForTheFirstBufferIsFetchedAgainWithABiggerOne()
        {
            // Larger than the dialog itself would ever allow, but it provokes exactly the failure
            // Windows reports whenever the first guess is too small - and ignoring that failure is
            // how a credential goes missing entirely.
            var typed = new string('p', 600);
            var dialog = new CredentialsDialog(new RealBufferCredUi { Password = typed });

            dialog.Show();

            Assert.Equal(typed, Reveal(dialog.Password));
        }

        [Fact]
        public void TypedUserNameReplacesTheOneSeededIntoTheDialog()
        {
            var dialog = new CredentialsDialog(new RealBufferCredUi { UserName = "alice" });

            dialog.Show(username: "seeded-by-the-caller");

            Assert.Equal("alice", dialog.UserName);
        }

        [Fact]
        public void CredentialBufferIsReleasedAfterASuccessfulPrompt()
        {
            var api = new RealBufferCredUi();
            var dialog = new CredentialsDialog(api);

            dialog.Show();

            Assert.Equal(api.AllocatedBuffers, api.FreedBuffers);
        }
    }
}
