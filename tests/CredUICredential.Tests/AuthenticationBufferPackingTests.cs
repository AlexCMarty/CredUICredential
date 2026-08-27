using System.Text;
using CredUICredential.Pinvoke;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     <c>CredPackAuthenticationBuffer</c> is what lets a caller seed the dialog's user name
    ///     field. This drives it against the real <c>credui.dll</c> and reads the result back with
    ///     the same unpack call the dialog itself uses, so the round trip is proven independently
    ///     of anything <see cref="CredentialsDialog"/> does with it.
    /// </summary>
    public class AuthenticationBufferPackingTests
    {
        [Fact]
        public void APackedUserNameRoundTripsThroughUnpack()
        {
            var api = CredUiApi.Instance;

            var packed = api.TryPackAuthenticationBuffer(
                "CONTOSO\\alice", out var buffer, out var size, out _);

            try
            {
                Assert.True(packed);

                var userName = new StringBuilder(CREDUI.MAX_USERNAME_LENGTH);
                var domain = new StringBuilder(CREDUI.MAX_DOMAIN_TARGET_LENGTH);
                var password = new StringBuilder(CREDUI.MAX_PASSWORD_LENGTH);
                var userNameCapacity = CREDUI.MAX_USERNAME_LENGTH;
                var domainCapacity = CREDUI.MAX_DOMAIN_TARGET_LENGTH;
                var passwordCapacity = CREDUI.MAX_PASSWORD_LENGTH;

                var unpacked = CREDUI.CredUnPackAuthenticationBuffer(
                    0, buffer, size,
                    userName, ref userNameCapacity,
                    domain, ref domainCapacity,
                    password, ref passwordCapacity);

                Assert.True(unpacked);
                Assert.Equal("CONTOSO\\alice", userName.ToString());
            }
            finally
            {
                api.FreeAuthenticationBuffer(buffer, size);
            }
        }
    }
}
