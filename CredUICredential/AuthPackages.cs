using System;
using CredUICredential.Pinvoke;

namespace CredUICredential
{
    /// <summary>
    ///     The authentication package ids this module seeds the credential dialog with, resolved
    ///     once from LSA.
    /// </summary>
    internal static class AuthPackages
    {
        private static readonly Lazy<uint> LazyKerberos = new(LookupKerberos);

        /// <summary>
        ///     Kerberos' package id on this machine, or <c>0</c> when LSA cannot be asked.
        /// </summary>
        /// <remarks>
        ///     Seeding the prompt with Kerberos is what keeps the PIN and smart-card tiles off
        ///     "More choices" while leaving the password provider - and its peek glyph - in place.
        ///     Falling back to <c>0</c> is Negotiate, the prompt's own default: the tiles come
        ///     back, but the dialog still works and the cmdlet still rejects what it must.
        /// </remarks>
        internal static uint Kerberos => LazyKerberos.Value;

        private static uint LookupKerberos()
        {
            if (SECUR32.LsaConnectUntrusted(out var handle) != 0)
            {
                return 0;
            }

            try
            {
                return SECUR32.TryLookupPackage(handle, "Kerberos", out var id) ? id : 0;
            }
            finally
            {
                SECUR32.LsaDeregisterLogonProcess(handle);
            }
        }
    }
}
