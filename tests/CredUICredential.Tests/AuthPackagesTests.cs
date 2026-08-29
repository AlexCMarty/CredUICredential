using CredUICredential.Pinvoke;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     The package ids the module seeds the dialog with, against this machine's LSA.
    /// </summary>
    public class AuthPackagesTests
    {
        [Fact]
        public void KerberosIsTheIdLsaReportsForIt()
        {
            Assert.Equal(0, SECUR32.LsaConnectUntrusted(out var handle));
            try
            {
                Assert.True(SECUR32.TryLookupPackage(handle, "Kerberos", out var expected));
                Assert.Equal(expected, AuthPackages.Kerberos);
            }
            finally
            {
                SECUR32.LsaDeregisterLogonProcess(handle);
            }
        }

        [Fact]
        public void KerberosIsNotNegotiate()
        {
            // Negotiate is 0, which is also the credential dialog's own default. Seeding it would
            // be indistinguishable from seeding nothing, and would leave the PIN tile in place.
            Assert.NotEqual(0u, AuthPackages.Kerberos);
        }
    }
}
