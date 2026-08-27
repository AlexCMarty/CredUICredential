using CredUICredential.Tests.Fakes;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     How the user name is put back together. <c>CredUnPackAuthenticationBuffer</c> reports the
    ///     domain in a buffer of its own, and whatever it puts there is part of who the user said
    ///     they were: a credential that arrives as CONTOSO\alice and leaves as alice is a different
    ///     account.
    /// </summary>
    public class CredentialIdentityTests
    {
        [Theory]
        // A domain reported on its own belongs in front of the user name.
        [InlineData("alice", "CONTOSO", "CONTOSO\\alice")]
        // Nothing to prepend.
        [InlineData("alice", "", "alice")]
        // Already qualified: prepending again would invent a domain of a domain.
        [InlineData("CONTOSO\\alice", "CONTOSO", "CONTOSO\\alice")]
        // A user principal name carries its own domain in the suffix.
        [InlineData("alice@contoso.com", "contoso.com", "alice@contoso.com")]
        // A local account: the machine name is the domain, and it still matters.
        [InlineData("Administrator", "WORKSTATION", "WORKSTATION\\Administrator")]
        public void TheDomainStaysAttachedToTheUserName(string userName, string domain, string expected)
        {
            var dialog = new CredentialsDialog(
                new ScriptedCredUi { UserName = userName, DomainName = domain });

            dialog.Show();

            Assert.Equal(expected, dialog.UserName);
        }
    }
}
