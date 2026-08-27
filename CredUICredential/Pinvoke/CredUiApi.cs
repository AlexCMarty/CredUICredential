using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CredUICredential.Pinvoke
{
    /// <summary> Calls the real <c>credui.dll</c>. </summary>
    internal sealed class CredUiApi : ICredUiApi
    {
        /// <summary>
        ///     Tells <c>CredUnPackAuthenticationBuffer</c> that the buffer holds protected
        ///     credentials, which is what the modern dialog produces. Without it the password comes
        ///     back still encrypted.
        /// </summary>
        private const int CRED_PACK_PROTECTED_CREDENTIALS = 0x1;

        public static readonly CredUiApi Instance = new();

        public CREDUI.ReturnCodes PromptForWindowsCredentials(
            ref CREDUI.INFO info,
            ref uint authPackage,
            IntPtr inAuthBuffer,
            uint inAuthBufferSize,
            out IntPtr authBuffer,
            out uint authBufferSize,
            ref bool save,
            CREDUI.FLAGS flags)
            => CREDUI.CredUIPromptForWindowsCredentials(
                ref info,
                0,
                ref authPackage,
                inAuthBuffer,
                inAuthBufferSize,
                out authBuffer,
                out authBufferSize,
                ref save,
                flags);

        public bool TryUnpackAuthenticationBuffer(
            IntPtr authBuffer,
            uint authBufferSize,
            StringBuilder userName,
            ref int userNameCapacity,
            StringBuilder domainName,
            ref int domainNameCapacity,
            StringBuilder password,
            ref int passwordCapacity,
            out int lastError)
        {
            var unpacked = CREDUI.CredUnPackAuthenticationBuffer(
                CRED_PACK_PROTECTED_CREDENTIALS,
                authBuffer,
                authBufferSize,
                userName,
                ref userNameCapacity,
                domainName,
                ref domainNameCapacity,
                password,
                ref passwordCapacity);

            lastError = unpacked ? 0 : Marshal.GetLastWin32Error();
            return unpacked;
        }

        public void FreeAuthenticationBuffer(IntPtr authBuffer, uint authBufferSize)
        {
            if (authBuffer == IntPtr.Zero)
            {
                return;
            }

            Scrub(authBuffer, authBufferSize);

            // The prompt allocates through the COM task allocator, which is what this releases.
            Marshal.FreeCoTaskMem(authBuffer);
        }

        public bool TryPackAuthenticationBuffer(
            string userName,
            out IntPtr authBuffer,
            out uint authBufferSize,
            out int lastError)
        {
            var required = 0;

            // The documented way to ask for the size: call with no buffer and expect failure.
            CREDUI.CredPackAuthenticationBuffer(0, userName, string.Empty, IntPtr.Zero, ref required);

            if (required <= 0)
            {
                authBuffer = IntPtr.Zero;
                authBufferSize = 0;
                lastError = Marshal.GetLastWin32Error();
                return false;
            }

            var buffer = Marshal.AllocCoTaskMem(required);
            if (!CREDUI.CredPackAuthenticationBuffer(0, userName, string.Empty, buffer, ref required))
            {
                lastError = Marshal.GetLastWin32Error();
                Marshal.FreeCoTaskMem(buffer);
                authBuffer = IntPtr.Zero;
                authBufferSize = 0;
                return false;
            }

            authBuffer = buffer;
            authBufferSize = (uint)required;
            lastError = 0;
            return true;
        }

        /// <summary>
        ///     Overwrites <paramref name="size"/> bytes at <paramref name="buffer"/> with zeroes.
        /// </summary>
        /// <remarks>
        ///     The authentication buffer holds the password in the clear, and freed task memory is
        ///     handed straight to whatever allocates next. Microsoft's guidance for
        ///     <c>CredUIPromptForWindowsCredentials</c> is to wipe the buffer before releasing it.
        /// </remarks>
        internal static void Scrub(IntPtr buffer, uint size)
        {
            if (buffer == IntPtr.Zero)
            {
                return;
            }

            for (uint offset = 0; offset < size; offset++)
            {
                Marshal.WriteByte(buffer, (int)offset, 0);
            }
        }
    }
}
