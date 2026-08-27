using System;
using System.Collections.Generic;
using System.Text;
using CredUICredential.Pinvoke;
using CredUICredential.Tests.Native;

namespace CredUICredential.Tests.Fakes
{
    /// <summary>
    ///     Stands in for the interactive prompt only. Everything downstream of it - decoding the
    ///     authentication buffer and releasing it - runs against the real <c>credui.dll</c>.
    /// </summary>
    /// <remarks>
    ///     This is the closest a test can get to the shipping behaviour without a human at the
    ///     keyboard: the dialog is replaced by a buffer that Windows itself packed, and the module
    ///     then does exactly what it does in production.
    /// </remarks>
    internal sealed class RealBufferCredUi : ICredUiApi
    {
        private readonly CredUiApi _real = CredUiApi.Instance;

        /// <summary>The user name the "user" types into the stand-in dialog.</summary>
        public string UserName { get; init; } = "alice";

        /// <summary>The password the "user" types into the stand-in dialog.</summary>
        public string Password { get; init; } = "s3cret";

        /// <summary>The state the stand-in dialog reports for the Save check box.</summary>
        public bool SaveChecked { get; set; }

        /// <summary>The flags the module asked the dialog to be shown with.</summary>
        public CREDUI.FLAGS? RequestedFlags { get; private set; }

        /// <summary>The dialog description the module built.</summary>
        public CREDUI.INFO? RequestedInfo { get; private set; }

        /// <summary>Every buffer handed to <see cref="FreeAuthenticationBuffer"/>, in order.</summary>
        public List<IntPtr> FreedBuffers { get; } = new();

        /// <summary>Every buffer this stand-in allocated, in order.</summary>
        public List<IntPtr> AllocatedBuffers { get; } = new();

        public CREDUI.ReturnCodes PromptForWindowsCredentials(
            ref CREDUI.INFO info,
            ref uint authPackage,
            out IntPtr authBuffer,
            out uint authBufferSize,
            ref bool save,
            CREDUI.FLAGS flags)
        {
            RequestedFlags = flags;
            RequestedInfo = info;

            authBuffer = CredentialBuffer.Pack(UserName, Password, out authBufferSize);
            AllocatedBuffers.Add(authBuffer);
            // Windows only touches fSave when the check box was actually requested; a stand-in
            // that always writes it would hide a stale value left over from an earlier prompt.
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
            => _real.TryUnpackAuthenticationBuffer(
                authBuffer, authBufferSize,
                userName, ref userNameCapacity,
                domainName, ref domainNameCapacity,
                password, ref passwordCapacity,
                out lastError);

        public void FreeAuthenticationBuffer(IntPtr authBuffer, uint authBufferSize)
        {
            FreedBuffers.Add(authBuffer);
            _real.FreeAuthenticationBuffer(authBuffer, authBufferSize);
        }
    }
}
