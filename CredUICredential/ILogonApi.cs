using System.Security;

namespace CredUICredential
{
    /// <summary>
    ///     Proves a user name and password against this machine (or its domain) and reports
    ///     whether the resulting token is a local administrator.
    /// </summary>
    internal interface ILogonApi
    {
        LogonResult TryLogon(string userName, SecureString password);
    }
}
