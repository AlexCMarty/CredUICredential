using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CredUICredential.Pinvoke
{
    internal static partial class CREDUI
    {
        /// <summary>
        ///     The limits Windows itself publishes for these fields, from <c>wincred.h</c>.
        /// </summary>
        /// <remarks>
        ///     These are ceilings, not buffer sizes to rely on blindly:
        ///     <c>CredUnPackAuthenticationBuffer</c> reports the size it actually needs when the
        ///     buffer it was handed is too small, and callers are expected to honour that.
        /// </remarks>
        public const int MAX_MESSAGE_LENGTH = 1024;

        public const int MAX_CAPTION_LENGTH = 128;
        public const int MAX_GENERIC_TARGET_LENGTH = 32767;
        public const int MAX_DOMAIN_TARGET_LENGTH = 337;
        public const int MAX_USERNAME_LENGTH = 513;
        public const int MAX_PASSWORD_LENGTH = 256;

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern ReturnCodes CredUIPromptForWindowsCredentials(ref CREDUI.INFO notUsedHere,
            int authError,
            ref uint authPackage,
            IntPtr InAuthBuffer,
            uint InAuthBufferSize,
            out IntPtr refOutAuthBuffer,
            out uint refOutAuthBufferSize,
            ref bool fSave,
            FLAGS flags);

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        internal static extern bool CredUnPackAuthenticationBuffer(int dwFlags,
            IntPtr pAuthBuffer,
            uint cbAuthBuffer,
            StringBuilder pszUserName,
            ref int pcchMaxUserName,
            StringBuilder pszDomainName,
            ref int pcchMaxDomainame,
            StringBuilder pszPassword,
            ref int pcchMaxPassword);
    }
}
