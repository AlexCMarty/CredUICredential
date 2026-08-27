using System.Net;
using System.Security;
using System.Text;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     The password arrives from Windows as plain characters and has to become a
    ///     <see cref="SecureString"/>. What happens in between is the whole point of the module
    ///     using SecureString at all.
    /// </summary>
    public class PlaintextHandlingTests
    {
        private static string Reveal(SecureString secure)
            => new NetworkCredential(string.Empty, secure).Password;

        [Fact]
        public void OverwritingABufferLeavesNoneOfTheOriginalBehind()
        {
            var buffer = new StringBuilder("hunter2");

            Plaintext.Overwrite(buffer);

            Assert.Equal("\0\0\0\0\0\0\0", buffer.ToString());
        }

        [Fact]
        public void OverwritingCopesWithAnEmptyBuffer()
        {
            var buffer = new StringBuilder();

            Plaintext.Overwrite(buffer);

            Assert.Equal(0, buffer.Length);
        }

        [Fact]
        public void ABufferBecomesASecureStringWithTheSameCharacters()
        {
            var buffer = new StringBuilder("correct horse");

            using var secure = Plaintext.ToSecureString(buffer);

            Assert.Equal("correct horse", Reveal(secure));
        }

        [Fact]
        public void ASecureStringBuiltFromABufferIsSealed()
        {
            // The credential is handed on to PSCredential and lives as long as the caller keeps
            // it. Nothing downstream has any business rewriting it.
            var buffer = new StringBuilder("correct horse");

            using var secure = Plaintext.ToSecureString(buffer);

            Assert.True(secure.IsReadOnly());
        }

        [Fact]
        public void ThePasswordAPromptHandsBackIsSealed()
        {
            var dialog = new CredentialsDialog(new RealBufferCredUi { Password = "s3cret" });

            dialog.Show();

            Assert.True(dialog.Password.IsReadOnly());
        }
    }
}
