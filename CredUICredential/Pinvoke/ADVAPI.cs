using System;
using System.Runtime.InteropServices;

namespace CredUICredential.Pinvoke
{
    /// <summary>
    ///     The slice of <c>advapi32.dll</c> used to prove a password and to read the groups on
    ///     the resulting token.
    /// </summary>
    internal static class ADVAPI
    {
        public const int ERROR_ELEVATION_REQUIRED = 740;
        public const int ERROR_LOGON_FAILURE = 1326;
        public const int ERROR_ACCOUNT_RESTRICTION = 1327;
        public const int ERROR_INVALID_LOGON_HOURS = 1328;
        public const int ERROR_ACCOUNT_DISABLED = 1331;
        public const int ERROR_PASSWORD_EXPIRED = 1330;
        public const int ERROR_ACCOUNT_EXPIRED = 1793;
        public const int ERROR_PASSWORD_MUST_CHANGE = 1907;
        public const int ERROR_ACCOUNT_LOCKED_OUT = 1909;

        public const int LOGON32_LOGON_NETWORK = 3;
        public const int LOGON32_PROVIDER_DEFAULT = 0;

        public const int TokenGroups = 2;

        public const uint SE_GROUP_USE_FOR_DENY_ONLY = 0x00000010;

        /// <summary>WinBuiltinAdministratorsSid. Local Administrators, S-1-5-32-544.</summary>
        public const int WinBuiltinAdministratorsSid = 26;

        /// <summary>WinWorldSid. Everyone, used by tests as a non-admin group.</summary>
        public const int WinWorldSid = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SID_AND_ATTRIBUTES
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TOKEN_GROUPS
        {
            public int GroupCount;
            public SID_AND_ATTRIBUTES Groups;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            IntPtr lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out IntPtr phToken);

        [DllImport("advapi32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            IntPtr tokenInformation,
            int tokenInformationLength,
            out int returnLength);

        [DllImport("advapi32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern bool CreateWellKnownSid(
            int wellKnownSidType,
            IntPtr domainSid,
            IntPtr pSid,
            ref int cbSid);

        [DllImport("advapi32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern bool EqualSid(IntPtr pSid1, IntPtr pSid2);

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
