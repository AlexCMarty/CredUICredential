using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     How a typed user name is turned into the two strings <c>LogonUser</c> wants.
    /// </summary>
    public class LogonIdentityTests
    {
        [Theory]
        [InlineData("CONTOSO\\alice", "alice", "CONTOSO")]
        [InlineData("WORKSTATION\\Administrator", "Administrator", "WORKSTATION")]
        [InlineData(".\\bob", "bob", ".")]
        public void ADownLevelNameSplitsAtTheBackslash(string typed, string userName, string domain)
        {
            var split = LogonIdentity.Split(typed);

            Assert.Equal(userName, split.UserName);
            Assert.Equal(domain, split.Domain);
        }

        [Theory]
        [InlineData("alice@contoso.com")]
        [InlineData("alice@contoso.com@factory")]
        public void AUserPrincipalNameIsPassedThroughWithNoDomain(string typed)
        {
            var split = LogonIdentity.Split(typed);

            Assert.Equal(typed, split.UserName);
            Assert.Null(split.Domain);
        }

        [Fact]
        public void AnUnqualifiedNameIsLookedUpInTheLocalSam()
        {
            var split = LogonIdentity.Split("alice");

            Assert.Equal("alice", split.UserName);
            Assert.Equal(".", split.Domain);
        }
    }
}
