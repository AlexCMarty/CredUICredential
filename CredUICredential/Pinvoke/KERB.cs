namespace CredUICredential.Pinvoke
{
    /// <summary>
    ///     The <c>KERB_LOGON_SUBMIT_TYPE</c> values that can appear as the leading field of the
    ///     authentication buffer <c>CredUIPromptForWindowsCredentials</c> hands back.
    /// </summary>
    /// <remarks>
    ///     Every credential the dialog packs begins with this tag, so it says what kind of
    ///     credential the buffer actually holds - which the auth package id does not. Only
    ///     <see cref="InteractiveLogon"/> is a user name and password.
    /// </remarks>
    internal static class KERB
    {
        /// <summary> <c>KerbInteractiveLogon</c> - a user name and password. </summary>
        internal const uint InteractiveLogon = 2;

        /// <summary> <c>KerbSmartCardLogon</c>. </summary>
        internal const uint SmartCardLogon = 6;
    }
}
