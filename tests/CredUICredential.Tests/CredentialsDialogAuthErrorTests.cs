using CredUICredential.Pinvoke;
using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     The modern prompt's <c>dwAuthError</c> is how Windows restyles the dialog after a
    ///     failed logon. It has to reach <c>credui.dll</c> unchanged.
    /// </summary>
    public class CredentialsDialogAuthErrorTests
    {
        [Fact]
        public void AFirstPromptPassesNoAuthError()
        {
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show();

            Assert.Equal(new[] { 0 }, api.RequestedAuthErrors);
        }

        [Fact]
        public void AuthErrorReachesTheNativePrompt()
        {
            var api = new ScriptedCredUi();

            new CredentialsDialog(api).Show(authError: ADVAPI.ERROR_LOGON_FAILURE);

            Assert.Equal(new[] { ADVAPI.ERROR_LOGON_FAILURE }, api.RequestedAuthErrors);
        }
    }
}
