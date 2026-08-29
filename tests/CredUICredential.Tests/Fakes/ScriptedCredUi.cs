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

        /// <summary>The pointer <see cref="TryPackAuthenticationBuffer"/> hands back on success.</summary>
        public static readonly IntPtr InputBuffer = new(0x0BADF00D);

        public const uint InputBufferSize = 64;

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

        /// <summary>When set, <see cref="TryPackAuthenticationBuffer"/> fails with this Win32 error.</summary>
        public int? PackFailsWith { get; set; }

        /// <summary>The user name last handed to <see cref="TryPackAuthenticationBuffer"/>, if any.</summary>
        public string PackedUserName { get; private set; }

        /// <summary>Every user name packed into an input buffer, in order.</summary>
        public List<string> PackedUserNames { get; } = new();

        /// <summary>The input buffer the module passed into the prompt, if any.</summary>
        public IntPtr? RequestedInAuthBuffer { get; private set; }

        /// <summary>The input buffer size the module passed into the prompt.</summary>
        public uint RequestedInAuthBufferSize { get; private set; }

        /// <summary>Every buffer handed to <see cref="FreeAuthenticationBuffer"/>, in order.</summary>
        public List<IntPtr> FreedBuffers { get; } = new();

        /// <summary>The buffer capacities offered on each decode attempt, in order.</summary>
        public List<Capacities> UnpackAttempts { get; } = new();

        public int PromptCount { get; private set; }

        /// <summary>Every <c>dwAuthError</c> the module passed into the prompt, in order.</summary>
        public List<int> RequestedAuthErrors { get; } = new();

        /// <summary>
        ///     When non-empty, the user name unpacked on each prompt, in order. Otherwise
        ///     <see cref="UserName"/> is used every time.
        /// </summary>
        public List<string> UserNamesByAttempt { get; } = new();

        /// <summary>
        ///     When non-empty, the password unpacked on each prompt, in order. Otherwise
        ///     <see cref="Password"/> is used every time.
        /// </summary>
        public List<string> PasswordsByAttempt { get; } = new();

        /// <summary>
        ///     When non-empty, the prompt result on each attempt, in order. Otherwise
        ///     <see cref="PromptResult"/> is used every time.
        /// </summary>
        public List<CREDUI.ReturnCodes> PromptResultsByAttempt { get; } = new();

        /// <summary>
        ///     When non-empty, the Save check box on each prompt, in order. Otherwise
        ///     <see cref="SaveChecked"/> is used every time.
        /// </summary>
        public List<bool> SaveCheckedByAttempt { get; } = new();

        /// <summary>
        ///     The <c>KERB_LOGON_SUBMIT_TYPE</c> tag <see cref="TryReadMessageType"/> reports.
        ///     Defaults to a password, so tests that do not care get one.
        /// </summary>
        public uint MessageType { get; set; } = KERB.InteractiveLogon;

        /// <summary>Per-attempt override of <see cref="MessageType"/> when non-empty.</summary>
        public List<uint> MessageTypesByAttempt { get; } = new();

        /// <summary>When set, the buffer is reported as too small to hold the tag.</summary>
        public bool MessageTypeUnreadable { get; set; }

        internal readonly record struct Capacities(int UserName, int Domain, int Password);

        public CREDUI.ReturnCodes PromptForWindowsCredentials(
            ref CREDUI.INFO info,
            int authError,
            ref uint authPackage,
            IntPtr inAuthBuffer,
            uint inAuthBufferSize,
            out IntPtr authBuffer,
            out uint authBufferSize,
            ref bool save,
            CREDUI.FLAGS flags)
        {
            PromptCount++;
            RequestedAuthErrors.Add(authError);
            RequestedInfo = info;
            RequestedInAuthBuffer = inAuthBuffer;
            RequestedInAuthBufferSize = inAuthBufferSize;

            var promptResult = At(PromptResultsByAttempt, PromptResult);
            if (promptResult != CREDUI.ReturnCodes.NO_ERROR)
            {
                // A failed prompt allocates nothing, so there is nothing for the caller to free.
                authBuffer = IntPtr.Zero;
                authBufferSize = 0;
                return promptResult;
            }

            authBuffer = Buffer;
            authBufferSize = BufferSize;

            if ((flags & CREDUI.FLAGS.CREDUIWIN_CHECKBOX) != 0)
            {
                save = At(SaveCheckedByAttempt, SaveChecked);
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

            var userNameValue = At(UserNamesByAttempt, UserName);
            var domainValue = DomainName;
            var passwordValue = At(PasswordsByAttempt, Password);

            var neededUserName = userNameValue.Length + 1;
            var neededDomain = domainValue.Length + 1;
            var neededPassword = passwordValue.Length + 1;

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

            userName.Clear().Append(userNameValue);
            domainName.Clear().Append(domainValue);
            password.Clear().Append(passwordValue);

            userNameCapacity = userNameValue.Length;
            domainNameCapacity = domainValue.Length;
            passwordCapacity = passwordValue.Length;
            lastError = 0;
            return true;
        }

        private T At<T>(List<T> sequence, T fallback)
        {
            if (sequence.Count == 0)
            {
                return fallback;
            }

            var index = Math.Max(PromptCount - 1, 0);
            return index < sequence.Count ? sequence[index] : sequence[^1];
        }

        public void FreeAuthenticationBuffer(IntPtr authBuffer, uint authBufferSize)
            => FreedBuffers.Add(authBuffer);

        public bool TryReadMessageType(IntPtr authBuffer, uint authBufferSize, out uint messageType)
        {
            messageType = 0;
            if (MessageTypeUnreadable)
            {
                return false;
            }

            messageType = At(MessageTypesByAttempt, MessageType);
            return true;
        }

        public bool TryPackAuthenticationBuffer(
            string userName,
            out IntPtr authBuffer,
            out uint authBufferSize,
            out int lastError)
        {
            PackedUserName = userName;
            PackedUserNames.Add(userName);

            if (PackFailsWith.HasValue)
            {
                authBuffer = IntPtr.Zero;
                authBufferSize = 0;
                lastError = PackFailsWith.Value;
                return false;
            }

            authBuffer = InputBuffer;
            authBufferSize = InputBufferSize;
            lastError = 0;
            return true;
        }
    }
}
