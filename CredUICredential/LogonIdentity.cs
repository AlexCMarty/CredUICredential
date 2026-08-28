namespace CredUICredential
{
    /// <summary>
    ///     The two strings <c>LogonUser</c> wants for a name the user typed.
    /// </summary>
    internal readonly record struct LogonIdentity(string UserName, string Domain)
    {
        /// <summary>
        ///     Splits a down-level, UPN or unqualified name the way <c>LogonUserW</c> documents.
        /// </summary>
        /// <remarks>
        ///     A domain of <see langword="null"/> means the user name is a UPN. A domain of
        ///     <c>"."</c> means the local SAM only, which is what an unqualified name is.
        /// </remarks>
        public static LogonIdentity Split(string userName)
        {
            var typed = userName ?? string.Empty;
            var slash = typed.IndexOf('\\');
            if (slash >= 0)
            {
                return new LogonIdentity(typed[(slash + 1)..], typed[..slash]);
            }

            if (typed.Contains('@'))
            {
                return new LogonIdentity(typed, Domain: null);
            }

            return new LogonIdentity(typed, ".");
        }
    }
}
