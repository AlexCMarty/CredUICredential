using CredUICredential.Pinvoke;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Only a wrong user name or password is worth asking again. Everything else
    ///     <c>LogonUser</c> can return is a condition typing will not fix.
    /// </summary>
    public class LogonResultTests
    {
        [Fact]
        public void AWrongPasswordIsRetryable()
        {
            var result = LogonResult.Failed(ADVAPI.ERROR_LOGON_FAILURE);

            Assert.Equal(LogonStatus.LogonFailure, result.Status);
            Assert.Equal(ADVAPI.ERROR_LOGON_FAILURE, result.NativeError);
            Assert.False(result.IsLocalAdministrator);
        }

        [Theory]
        [InlineData(ADVAPI.ERROR_ACCOUNT_LOCKED_OUT)]
        [InlineData(ADVAPI.ERROR_ACCOUNT_DISABLED)]
        [InlineData(ADVAPI.ERROR_PASSWORD_EXPIRED)]
        [InlineData(ADVAPI.ERROR_PASSWORD_MUST_CHANGE)]
        [InlineData(ADVAPI.ERROR_INVALID_LOGON_HOURS)]
        [InlineData(ADVAPI.ERROR_ACCOUNT_EXPIRED)]
        [InlineData(ADVAPI.ERROR_ACCOUNT_RESTRICTION)]
        [InlineData(5)]
        public void AnyOtherLogonErrorStopsTheLoop(int nativeError)
        {
            var result = LogonResult.Failed(nativeError);

            Assert.Equal(LogonStatus.NonRetryable, result.Status);
            Assert.Equal(nativeError, result.NativeError);
            Assert.False(result.IsLocalAdministrator);
        }

        [Fact]
        public void ASuccessfulLogonCanBeAnAdministrator()
        {
            var result = LogonResult.Succeeded(isLocalAdministrator: true);

            Assert.Equal(LogonStatus.Success, result.Status);
            Assert.Equal(0, result.NativeError);
            Assert.True(result.IsLocalAdministrator);
        }

        [Fact]
        public void ASuccessfulLogonCanBeAnOrdinaryUser()
        {
            var result = LogonResult.Succeeded(isLocalAdministrator: false);

            Assert.Equal(LogonStatus.Success, result.Status);
            Assert.False(result.IsLocalAdministrator);
        }
    }
}
