using System;
using CredUICredential.Pinvoke;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     What the module asks Windows to draw: the caption, the message and the display flags.
    ///     These reach the operating system through the CREDUI_INFO structure and the flags
    ///     argument, so the stand-in records both and the tests read them straight back.
    /// </summary>
    public class CredentialsDialogDescriptionTests
    {
        private static CREDUI.INFO Describe(CredentialsDialog dialog, RealBufferCredUi api, bool showSaveCheckbox = false)
        {
            dialog.Show(showSaveCheckbox: showSaveCheckbox);
            return Assert.NotNull(api.RequestedInfo);
        }

        [Fact]
        public void DefaultCaptionIsUsedWhenTheCallerSuppliesNone()
        {
            var api = new RealBufferCredUi();

            var info = Describe(new CredentialsDialog(api), api);

            Assert.Equal("Credentials", info.pszCaptionText);
        }

        [Fact]
        public void DefaultMessageIsUsedWhenTheCallerSuppliesNone()
        {
            var api = new RealBufferCredUi();

            var info = Describe(new CredentialsDialog(api), api);

            Assert.Equal("Enter your credentials.", info.pszMessageText);
        }

        [Fact]
        public void SuppliedCaptionReachesTheDialog()
        {
            var api = new RealBufferCredUi();

            var info = Describe(new CredentialsDialog(api, caption: "Admin credentials needed"), api);

            Assert.Equal("Admin credentials needed", info.pszCaptionText);
        }

        [Fact]
        public void SuppliedMessageReachesTheDialog()
        {
            var api = new RealBufferCredUi();

            var info = Describe(new CredentialsDialog(api, message: "Enter your admin credentials"), api);

            Assert.Equal("Enter your admin credentials", info.pszMessageText);
        }

        [Fact]
        public void AMessageLongerThanAHundredCharactersIsStillAccepted()
        {
            // Windows allows a 1024-character message. Rejecting a paragraph of explanation would
            // be the module's own limitation, not the platform's.
            var message = new string('m', 300);
            var api = new RealBufferCredUi();

            var info = Describe(new CredentialsDialog(api, message: message), api);

            Assert.Equal(message, info.pszMessageText);
        }

        [Fact]
        public void ACaptionLongerThanAHundredCharactersIsStillAccepted()
        {
            // Windows allows a 128-character caption. Password managers key off the window title,
            // so the ones that need a long, specific title should get it.
            var caption = new string('c', 120);
            var api = new RealBufferCredUi();

            var info = Describe(new CredentialsDialog(api, caption: caption), api);

            Assert.Equal(caption, info.pszCaptionText);
        }

        [Fact]
        public void AMessageBeyondTheWindowsLimitIsRejected()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => new CredentialsDialog(new RealBufferCredUi(), message: new string('m', 1025)));

            Assert.Equal("Message", exception.ParamName);
        }

        [Fact]
        public void ACaptionBeyondTheWindowsLimitIsRejected()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => new CredentialsDialog(new RealBufferCredUi(), caption: new string('c', 129)));

            Assert.Equal("Caption", exception.ParamName);
        }

        [Fact]
        public void TheDialogRequestsThePasswordProviderUi()
        {
            // CREDUIWIN_AUTHPACKAGE_ONLY is the password-provider surface (including peek).
            // PIN / smart-card "More choices" may still appear; the cmdlet rejects those after OK.
            var api = new RealBufferCredUi();

            new CredentialsDialog(api).Show();

            Assert.Equal(CREDUI.FLAGS.CREDUIWIN_AUTHPACKAGE_ONLY, api.RequestedFlags);
        }

        [Fact]
        public void TheSaveCheckBoxIsRequestedOnlyWhenAskedFor()
        {
            var api = new RealBufferCredUi();

            new CredentialsDialog(api).Show(showSaveCheckbox: true);

            Assert.Equal(
                CREDUI.FLAGS.CREDUIWIN_AUTHPACKAGE_ONLY | CREDUI.FLAGS.CREDUIWIN_CHECKBOX,
                api.RequestedFlags);
        }

        [Fact]
        public void TheCheckedSaveBoxIsReportedBack()
        {
            var dialog = new CredentialsDialog(new RealBufferCredUi { SaveChecked = true });

            dialog.Show(showSaveCheckbox: true);

            Assert.True(dialog.SaveChecked);
        }

        [Fact]
        public void TheUncheckedSaveBoxIsReportedBack()
        {
            var dialog = new CredentialsDialog(new RealBufferCredUi { SaveChecked = false });

            dialog.Show(showSaveCheckbox: true);

            Assert.False(dialog.SaveChecked);
        }

        [Fact]
        public void SaveCheckedDoesNotLingerFromAnEarlierPrompt()
        {
            // Windows leaves fSave alone when the check box was not requested, so a dialog reused
            // for a second prompt would otherwise keep reporting the first prompt's answer.
            var api = new RealBufferCredUi { SaveChecked = true };
            var dialog = new CredentialsDialog(api);
            dialog.Show(showSaveCheckbox: true);

            dialog.Show();

            Assert.False(dialog.SaveChecked);
        }

        [Fact]
        public void TheDialogDescribesItsOwnSizeToWindows()
        {
            // CREDUI_INFO is versioned by its size; getting cbSize wrong makes the call fail with
            // ERROR_INVALID_PARAMETER at runtime, which no other test here would notice.
            var api = new RealBufferCredUi();

            var info = Describe(new CredentialsDialog(api), api);

            Assert.Equal(System.Runtime.InteropServices.Marshal.SizeOf<CREDUI.INFO>(), info.cbSize);
        }

        [Fact]
        public void TheMessageTypeIsTheTagFromTheBufferTheDialogReturned()
        {
            var api = new ScriptedCredUi { MessageType = KERB.SmartCardLogon };
            var dialog = new CredentialsDialog(api);

            dialog.Show();

            Assert.Equal(KERB.SmartCardLogon, dialog.MessageType);
        }

        [Fact]
        public void TheMessageTypeIsZeroWhenTheBufferIsTooSmallToHoldTheTag()
        {
            // Zero is not a valid KERB_LOGON_SUBMIT_TYPE, so an unreadable buffer reads as
            // "not a password" rather than silently passing the cmdlet's check.
            var api = new ScriptedCredUi { MessageTypeUnreadable = true };
            var dialog = new CredentialsDialog(api);

            dialog.Show();

            Assert.Equal(0u, dialog.MessageType);
        }

        [Fact]
        public void ABufferWindowsPackedFromAPasswordIsAnInteractiveLogon()
        {
            // The real contract, against the real credui.dll: a username-and-password buffer is
            // tagged KerbInteractiveLogon. This is what the cmdlet's check leans on.
            var api = new RealBufferCredUi();
            var dialog = new CredentialsDialog(api);

            dialog.Show();

            Assert.Equal(KERB.InteractiveLogon, dialog.MessageType);
        }

        [Fact]
        public void TheDialogSeedsKerberosAsTheInputAuthPackage()
        {
            // Seeding Kerberos is what keeps the PIN and smart-card tiles off "More choices"
            // while leaving the password provider - and its peek glyph - in place.
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show();

            Assert.Equal(AuthPackages.Kerberos, Assert.Single(api.SeededAuthPackages));
        }
    }
}
