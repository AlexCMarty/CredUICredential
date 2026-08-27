using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CredUICredential.Tests.Native
{
    /// <summary>
    ///     Builds the kind of authentication buffer that the Windows credential dialog hands back,
    ///     using the same <c>credui.dll</c> that produces the real thing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>CredPackAuthenticationBuffer</c> is the non-interactive counterpart of the
    ///         dialog: it encodes a user name and password into exactly the format that
    ///         <c>CredUnPackAuthenticationBuffer</c> reads back. That lets tests drive the module's
    ///         real decoding path - real marshalling, real protected-credential handling, real
    ///         buffer sizing - without a human dismissing a prompt.
    ///     </para>
    ///     <para>
    ///         The buffer is allocated with the COM task allocator, so the code under test can
    ///         release it the same way it releases the operating system's own buffer.
    ///     </para>
    /// </remarks>
    internal static class CredentialBuffer
    {
        /// <summary>
        ///     Matches the flag the module passes when unpacking. The modern dialog produces
        ///     protected credentials, so the test buffers have to be protected too.
        /// </summary>
        private const int CRED_PACK_PROTECTED_CREDENTIALS = 0x1;

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredPackAuthenticationBufferW(
            int dwFlags,
            string pszUserName,
            string pszPassword,
            IntPtr pPackedCredentials,
            ref int pcbPackedCredentials);

        /// <summary>
        ///     Packs <paramref name="userName"/> and <paramref name="password"/> into a freshly
        ///     allocated task-memory buffer. The caller owns the result.
        /// </summary>
        public static IntPtr Pack(string userName, string password, out uint size)
        {
            var required = 0;

            // The documented way to ask for the size: call with no buffer and expect failure.
            CredPackAuthenticationBufferW(
                CRED_PACK_PROTECTED_CREDENTIALS, userName, password, IntPtr.Zero, ref required);

            if (required <= 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "CredPackAuthenticationBuffer would not report a buffer size.");
            }

            var buffer = Marshal.AllocCoTaskMem(required);
            if (!CredPackAuthenticationBufferW(
                    CRED_PACK_PROTECTED_CREDENTIALS, userName, password, buffer, ref required))
            {
                var error = Marshal.GetLastWin32Error();
                Marshal.FreeCoTaskMem(buffer);
                throw new Win32Exception(error, "CredPackAuthenticationBuffer failed.");
            }

            size = (uint)required;
            return buffer;
        }
    }
}
