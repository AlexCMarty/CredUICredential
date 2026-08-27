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
            out IntPtr authBuffer,
            out uint authBufferSize,
            ref bool save,
            CREDUI.FLAGS flags)
            => CREDUI.CredUIPromptForWindowsCredentials(
                ref info,
                0,
                ref authPackage,
                IntPtr.Zero,
                0,
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

            CREDUI.CoTaskMemFree(authBuffer);
        }
    }
}
