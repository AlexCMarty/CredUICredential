using System.Runtime.InteropServices;
using System.Security;
using CredUICredential.Pinvoke;

namespace CredUICredential
{
    /// <summary>
    ///     The shipping <see cref="ILogonApi"/>: <c>LogonUser</c> plus a walk of <c>TokenGroups</c>.
    /// </summary>
    internal sealed class LogonApi : ILogonApi
    {
        public static readonly LogonApi Instance = new();

        public LogonResult TryLogon(string userName, SecureString password)
        {
            var identity = LogonIdentity.Split(userName);
            var unmanaged = Marshal.SecureStringToGlobalAllocUnicode(password ?? new SecureString());
            try
            {
                if (!ADVAPI.LogonUser(
                    identity.UserName,
                    identity.Domain,
                    unmanaged,
                    ADVAPI.LOGON32_LOGON_NETWORK,
                    ADVAPI.LOGON32_PROVIDER_DEFAULT,
                    out var token))
                {
                    return LogonResult.Failed(Marshal.GetLastWin32Error());
                }

                try
                {
                    if (!TokenGroups.TryReadBuiltinAdministrators(token, out var isAdmin, out var readError))
                    {
                        return LogonResult.Failed(readError);
                    }

                    return LogonResult.Succeeded(isAdmin);
                }
                finally
                {
                    ADVAPI.CloseHandle(token);
                }
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanaged);
            }
        }
    }
}
