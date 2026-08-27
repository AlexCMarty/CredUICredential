using System;
using System.Runtime.InteropServices;
using CredUICredential.Pinvoke;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     The authentication buffer is allocated by Windows, owned by this module, and holds the
    ///     credential in the clear. Losing track of one leaks the password into the process for as
    ///     long as it lives.
    /// </summary>
    public class CredentialBufferLifetimeTests
    {
        [Fact]
        public void BufferIsReleasedAfterTheCredentialIsRead()
        {
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show();

            Assert.Equal(new[] { ScriptedCredUi.Buffer }, api.FreedBuffers);
        }

        [Fact]
        public void BufferIsReleasedEvenWhenItCannotBeDecoded()
        {
            // The decode failing is exactly when the buffer matters most: it is still sitting
            // there holding the password, and nothing else will ever come back for it.
            var api = new ScriptedCredUi
            {
                UnpackFailsWith = (int)CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER,
            };

            try
            {
                new CredentialsDialog(api).Show();
            }
            catch (Exception)
            {
                // How the failure is reported is another test's business.
            }

            Assert.Equal(new[] { ScriptedCredUi.Buffer }, api.FreedBuffers);
        }

        [Fact]
        public void NothingIsReleasedWhenThePromptAllocatedNothing()
        {
            var api = new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_CANCELLED };

            new CredentialsDialog(api).Show();

            Assert.Empty(api.FreedBuffers);
        }

        [Fact]
        public void TheSecondDecodeAttemptAsksForExactlyTheSizeWindowsReported()
        {
            var api = new ScriptedCredUi { Password = new string('p', 400) };

            new CredentialsDialog(api).Show();

            Assert.Collection(
                api.UnpackAttempts,
                first => Assert.Equal(CREDUI.MAX_PASSWORD_LENGTH, first.Password),
                second => Assert.Equal(401, second.Password));
        }

        [Fact]
        public void ADecodeThatFitsFirstTimeIsNotRepeated()
        {
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show();

            Assert.Single(api.UnpackAttempts);
        }

        [Fact]
        public void NoUserNameMeansNoInputBufferIsBuilt()
        {
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show();

            Assert.Null(api.PackedUserName);
        }

        [Fact]
        public void ThePackedInputBufferIsPassedToThePrompt()
        {
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show(username: "alice");

            Assert.Equal("alice", api.PackedUserName);
            Assert.Equal(ScriptedCredUi.InputBuffer, api.RequestedInAuthBuffer);
            Assert.Equal(ScriptedCredUi.InputBufferSize, api.RequestedInAuthBufferSize);
        }

        [Fact]
        public void TheInputBufferIsReleasedAfterASuccessfulPrompt()
        {
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show(username: "alice");

            Assert.Contains(ScriptedCredUi.InputBuffer, api.FreedBuffers);
        }

        [Fact]
        public void TheInputBufferIsReleasedEvenWhenThePromptIsCancelled()
        {
            var api = new ScriptedCredUi { PromptResult = CREDUI.ReturnCodes.ERROR_CANCELLED };

            new CredentialsDialog(api).Show(username: "alice");

            Assert.Contains(ScriptedCredUi.InputBuffer, api.FreedBuffers);
        }

        [Fact]
        public void NothingIsReleasedWhenPackingFails()
        {
            const int ERROR_NOT_ENOUGH_MEMORY = 8;
            var api = new ScriptedCredUi { PackFailsWith = ERROR_NOT_ENOUGH_MEMORY };

            try
            {
                new CredentialsDialog(api).Show(username: "alice");
            }
            catch (Exception)
            {
                // How the failure is reported is another test's business.
            }

            Assert.Empty(api.FreedBuffers);
        }

        [Fact]
        public void ReleasingABufferWipesTheCredentialOutOfItFirst()
        {
            // Microsoft's guidance for CredUIPromptForWindowsCredentials is to zero the buffer
            // before handing it back, so the plaintext is not left behind in freed memory for
            // whatever allocates that block next.
            const int size = 64;
            var buffer = Marshal.AllocCoTaskMem(size);
            try
            {
                for (var i = 0; i < size; i++)
                {
                    Marshal.WriteByte(buffer, i, 0xAB);
                }

                CredUiApi.Scrub(buffer, size);

                for (var i = 0; i < size; i++)
                {
                    Assert.Equal(0, Marshal.ReadByte(buffer, i));
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
    }
}
