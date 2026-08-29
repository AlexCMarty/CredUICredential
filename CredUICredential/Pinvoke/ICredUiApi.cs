using System;
using System.Text;

namespace CredUICredential.Pinvoke
{
    /// <summary>
    ///     The slice of <c>credui.dll</c> that <see cref="CredentialsDialog"/> depends on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Prompting for credentials is a modal, interactive operation, so the dialog's own
    ///         logic - flag selection, buffer sizing, error mapping and cleanup - can only be
    ///         tested if the native calls sit behind something replaceable. This interface is that
    ///         seam; <see cref="CredUiApi"/> is the one implementation that ships.
    ///     </para>
    ///     <para>
    ///         The members mirror the native contract closely, including the in/out length
    ///         parameters, so that a stand-in can behave the way Windows documents rather than the
    ///         way that happens to be convenient.
    ///     </para>
    /// </remarks>
    internal interface ICredUiApi
    {
        /// <summary>
        ///     Raises the modern Windows credential dialog. See
        ///     <c>CredUIPromptForWindowsCredentials</c>.
        /// </summary>
        /// <param name="authBuffer">
        ///     Receives a buffer allocated by the operating system. The caller owns it and must
        ///     hand it back to <see cref="FreeAuthenticationBuffer"/>.
        /// </param>
        /// <param name="inAuthBuffer">
        ///     A buffer to seed the dialog with, from <see cref="TryPackAuthenticationBuffer"/>, or
        ///     <see cref="IntPtr.Zero"/> for none.
        /// </param>
        /// <param name="authError">
        ///     A Win32 error to display on the dialog, or zero for none. See
        ///     <c>CredUIPromptForWindowsCredentials</c>'s <c>dwAuthError</c>.
        /// </param>
        CREDUI.ReturnCodes PromptForWindowsCredentials(
            ref CREDUI.INFO info,
            int authError,
            ref uint authPackage,
            IntPtr inAuthBuffer,
            uint inAuthBufferSize,
            out IntPtr authBuffer,
            out uint authBufferSize,
            ref bool save,
            CREDUI.FLAGS flags);

        /// <summary>
        ///     Decodes the buffer produced by <see cref="PromptForWindowsCredentials"/>. See
        ///     <c>CredUnPackAuthenticationBuffer</c>.
        /// </summary>
        /// <param name="userNameCapacity">
        ///     On entry, the size of <paramref name="userName"/> in characters. On a
        ///     <c>ERROR_INSUFFICIENT_BUFFER</c> failure, Windows overwrites this with the size it
        ///     needs, including the terminating null. The same applies to the domain and password
        ///     capacities.
        /// </param>
        /// <param name="lastError">
        ///     The Win32 error captured immediately after the call, so that callers do not have to
        ///     rely on the thread's last-error slot surviving the trip back.
        /// </param>
        /// <returns> <see langword="true"/> if the buffer was decoded. </returns>
        bool TryUnpackAuthenticationBuffer(
            IntPtr authBuffer,
            uint authBufferSize,
            StringBuilder userName,
            ref int userNameCapacity,
            StringBuilder domainName,
            ref int domainNameCapacity,
            StringBuilder password,
            ref int passwordCapacity,
            out int lastError);

        /// <summary>
        ///     Releases a buffer obtained from <see cref="PromptForWindowsCredentials"/>.
        /// </summary>
        void FreeAuthenticationBuffer(IntPtr authBuffer, uint authBufferSize);

        /// <summary>
        ///     Encodes a user name into the buffer format <see cref="PromptForWindowsCredentials"/>
        ///     accepts as an input buffer to seed. See <c>CredPackAuthenticationBuffer</c>.
        /// </summary>
        /// <param name="authBuffer">
        ///     Receives a buffer allocated by this call. The caller owns it and must hand it back to
        ///     <see cref="FreeAuthenticationBuffer"/>.
        /// </param>
        /// <param name="lastError">
        ///     The Win32 error captured immediately after a failed call.
        /// </param>
        /// <returns> <see langword="true"/> if the user name was packed. </returns>
        bool TryPackAuthenticationBuffer(
            string userName,
            out IntPtr authBuffer,
            out uint authBufferSize,
            out int lastError);

        /// <summary>
        ///     Reads the leading <c>KERB_LOGON_SUBMIT_TYPE</c> tag from a buffer produced by
        ///     <see cref="PromptForWindowsCredentials"/>, which says what kind of credential the
        ///     buffer actually holds - a password, a smart card, or something else entirely.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> if the buffer was big enough to hold the tag.
        /// </returns>
        bool TryReadMessageType(IntPtr authBuffer, uint authBufferSize, out uint messageType);
    }
}
