using System.Security;
using System.Text;

namespace CredUICredential
{
    /// <summary>
    ///     Handling for the short window in which the credential exists as plain characters.
    /// </summary>
    /// <remarks>
    ///     Windows hands the password back as text, and it has to be read out of a buffer before it
    ///     can become a <see cref="SecureString"/>. Everything in between is what the module is
    ///     using SecureString to avoid, so it happens here and it happens carefully: no
    ///     intermediate <see cref="string"/>, which would be immutable and sit on the heap until a
    ///     collection nobody controls, and the buffer is overwritten once it has been read.
    /// </remarks>
    internal static class Plaintext
    {
        /// <summary>
        ///     Copies <paramref name="buffer"/> into a sealed <see cref="SecureString"/>, one
        ///     character at a time.
        /// </summary>
        public static SecureString ToSecureString(StringBuilder buffer)
        {
            var secure = new SecureString();
            for (var index = 0; index < buffer.Length; index++)
            {
                secure.AppendChar(buffer[index]);
            }

            // The credential is handed on to PSCredential and lives as long as the caller keeps
            // it; nothing downstream has any business rewriting it.
            secure.MakeReadOnly();
            return secure;
        }

        /// <summary>
        ///     Overwrites everything in <paramref name="buffer"/> with null characters.
        /// </summary>
        /// <remarks>
        ///     Clearing a <see cref="StringBuilder"/> only resets its length: the characters stay
        ///     in the backing array until something else happens to write over them. Overwrite them
        ///     deliberately instead.
        /// </remarks>
        public static void Overwrite(StringBuilder buffer)
        {
            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index] = '\0';
            }
        }
    }
}
