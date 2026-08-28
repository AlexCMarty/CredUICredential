using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CredUICredential.Pinvoke;

namespace CredUICredential
{
    /// <summary>
    ///     Walks a <c>TOKEN_GROUPS</c> buffer the way <c>GetTokenInformation</c> lays one out.
    /// </summary>
    internal static class TokenGroups
    {
        /// <summary>
        ///     Whether the buffer includes the local Administrators SID, regardless of whether that
        ///     membership is enabled or deny-only.
        /// </summary>
        public static bool ContainsBuiltinAdministrators(IntPtr tokenGroups)
        {
            if (tokenGroups == IntPtr.Zero)
            {
                return false;
            }

            using var administrators = WellKnownSid.Alloc(ADVAPI.WinBuiltinAdministratorsSid);
            var count = Marshal.ReadInt32(tokenGroups);
            var header = Marshal.OffsetOf<ADVAPI.TOKEN_GROUPS>(nameof(ADVAPI.TOKEN_GROUPS.Groups)).ToInt32();
            var entrySize = Marshal.SizeOf<ADVAPI.SID_AND_ATTRIBUTES>();

            for (var i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<ADVAPI.SID_AND_ATTRIBUTES>(
                    tokenGroups + header + (i * entrySize));
                if (entry.Sid != IntPtr.Zero && ADVAPI.EqualSid(entry.Sid, administrators.DangerousGetHandle()))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Reads <c>TokenGroups</c> off <paramref name="token"/> and reports local
        ///     Administrators membership. Fails with the Win32 error if the token cannot be read.
        /// </summary>
        public static bool TryReadBuiltinAdministrators(IntPtr token, out bool isLocalAdministrator, out int nativeError)
        {
            isLocalAdministrator = false;
            nativeError = 0;

            ADVAPI.GetTokenInformation(token, ADVAPI.TokenGroups, IntPtr.Zero, 0, out var length);
            nativeError = Marshal.GetLastWin32Error();
            if (length <= 0)
            {
                return false;
            }

            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!ADVAPI.GetTokenInformation(token, ADVAPI.TokenGroups, buffer, length, out _))
                {
                    nativeError = Marshal.GetLastWin32Error();
                    return false;
                }

                isLocalAdministrator = ContainsBuiltinAdministrators(buffer);
                nativeError = 0;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    ///     A well-known SID allocated on the native heap.
    /// </summary>
    internal sealed class WellKnownSid : IDisposable
    {
        private IntPtr _sid;

        private WellKnownSid(IntPtr sid) => _sid = sid;

        public IntPtr DangerousGetHandle() => _sid;

        public static WellKnownSid Alloc(int wellKnownSidType)
        {
            var size = 0;
            ADVAPI.CreateWellKnownSid(wellKnownSidType, IntPtr.Zero, IntPtr.Zero, ref size);
            var sid = Marshal.AllocHGlobal(size);
            if (!ADVAPI.CreateWellKnownSid(wellKnownSidType, IntPtr.Zero, sid, ref size))
            {
                var error = Marshal.GetLastWin32Error();
                Marshal.FreeHGlobal(sid);
                throw new Win32Exception(error);
            }

            return new WellKnownSid(sid);
        }

        public void Dispose()
        {
            if (_sid != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_sid);
                _sid = IntPtr.Zero;
            }
        }
    }
}
