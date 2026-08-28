using System.Security;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Hits the real <c>LogonUser</c> import. The x86 CI job is what proves the calling
    ///     convention; a nonsense local account is enough to get a logon failure back.
    /// </summary>
    public class NativeLogonTests
    {
        [Fact]
        public void ANonsenseLocalAccountFailsWithLogonFailure()
        {
            using var password = MakeSecure("not-a-real-password");

            var result = LogonApi.Instance.TryLogon(
                "CredUICredential-NoSuchUser-DoNotCreate",
                password);

            Assert.Equal(LogonStatus.LogonFailure, result.Status);
            Assert.Equal(Pinvoke.ADVAPI.ERROR_LOGON_FAILURE, result.NativeError);
            Assert.False(result.IsLocalAdministrator);
        }

        private static SecureString MakeSecure(string value)
        {
            var secure = new SecureString();
            foreach (var c in value)
            {
                secure.AppendChar(c);
            }

            secure.MakeReadOnly();
            return secure;
        }
    }
}
