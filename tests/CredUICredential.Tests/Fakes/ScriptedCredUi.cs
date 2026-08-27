using System;
using System.Collections.Generic;
using System.Text;
using CredUICredential.Pinvoke;

namespace CredUICredential.Tests.Fakes
{
    /// <summary>
    ///     A fully scripted <c>credui.dll</c>, for the outcomes the real one cannot be talked into
    ///     producing on demand: a cancelled prompt, a prompt that fails outright, a buffer that
    ///     will not decode, a domain reported separately from the user name.
    /// </summary>
    /// <remarks>
    ///     Where it does answer, it answers the way Windows documents. In particular it implements
    ///     the <c>ERROR_INSUFFICIENT_BUFFER</c> protocol properly: it refuses to write into a
    ///     buffer that is too small, and reports the size it needs - including the terminating
    ///     null - through the capacity arguments.
    /// </remarks>
    internal sealed class ScriptedCredUi : ICredUiApi
    {
        private const int ERROR_INSUFFICIENT_BUFFER = (int)CREDUI.ReturnCodes.ERROR_INSUFFICIENT_BUFFER;

        /// <summary>
        ///     A pointer that is handed out and passed around but never dereferenced, standing in
        ///     for the block Windows would have allocated.
        /// </summary>
        public static readonly IntPtr Buffer = new(0x0BADC0DE);

        public const uint BufferSize = 128;

        /// <summary>What the prompt itself returns.</summary>
        public CREDUI.ReturnCodes PromptResult { get; set; } = CREDUI.ReturnCodes.NO_ERROR;

        /// <summary>The user name the packed buffer decodes to.</summary>
        public string UserName { get; set; } = "alice";

        /// <summary>The domain the packed buffer decodes to, reported separately.</summary>
        public string DomainName { get; set; } = string.Empty;

        /// <summary>The password the packed buffer decodes to.</summary>
        public string Password { get; set; } = "s3cret";

        /// <summary>The dialog description the module built.</summary>
        public CREDUI.INFO? RequestedInfo { get; private set; }

        /// <summary>The state the prompt reports for the Save check box.</summary>
        public bool SaveChecked { get; set; }

        /// <summary>When set, every decode attempt fails with this Win32 error.</summary>
        public int? UnpackFailsWith { get; set; }

        /// <summary>Every buffer handed to <see cref="FreeAuthenticationBuffer"/>, in order.</summary>
        public List<IntPtr> FreedBuffers { get; } = new();

        /// <summary>The buffer capacities offered on each decode attempt, in order.</summary>
        public List<Capacities> UnpackAttempts { get; } = new();

        public int PromptCount { get; private set; }

        internal readonly record struct Capacities(int UserName, int Domain, int Password);

        public CREDUI.ReturnCodes PromptForWindowsCredentials(
            ref CREDUI.INFO info,
            ref uint authPackage,
            out IntPtr authBuffer,
            out uint authBufferSize,
            ref bool save,
            CREDUI.FLAGS flags)
        {
            PromptCount++;
            RequestedInfo = info;

            if (PromptResult != CREDUI.ReturnCodes.NO_ERROR)
            {
                // A failed prompt allocates nothing, so there is nothing for the caller to free.
                authBuffer = IntPtr.Zero;
                authBufferSize = 0;
                return PromptResult;
            }

            authBuffer = Buffer;
            authBufferSize = BufferSize;

            if ((flags & CREDUI.FLAGS.CREDUIWIN_CHECKBOX) != 0)
            {
                save = SaveChecked;
            }

            return CREDUI.ReturnCodes.NO_ERROR;
        }

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
            UnpackAttempts.Add(new Capacities(userNameCapacity, domainNameCapacity, passwordCapacity));

            if (UnpackFailsWith.HasValue)
            {
                lastError = UnpackFailsWith.Value;
                return false;
            }

            var neededUserName = UserName.Length + 1;
            var neededDomain = DomainName.Length + 1;
            var neededPassword = Password.Length + 1;

            if (userNameCapacity < neededUserName
                || domainNameCapacity < neededDomain
                || passwordCapacity < neededPassword)
            {
                userNameCapacity = neededUserName;
                domainNameCapacity = neededDomain;
                passwordCapacity = neededPassword;
                lastError = ERROR_INSUFFICIENT_BUFFER;
                return false;
            }

            userName.Clear().Append(UserName);
            domainName.Clear().Append(DomainName);
            password.Clear().Append(Password);

            userNameCapacity = UserName.Length;
            domainNameCapacity = DomainName.Length;
            passwordCapacity = Password.Length;
            lastError = 0;
            return true;
        }

        public void FreeAuthenticationBuffer(IntPtr authBuffer, uint authBufferSize)
            => FreedBuffers.Add(authBuffer);
    }
}
