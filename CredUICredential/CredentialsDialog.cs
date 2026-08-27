using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using CredUICredential.Pinvoke;

namespace CredUICredential
{
    /// <summary>
    ///     Encapsulates dialog functionality from the Credential Management API.
    /// </summary>
    public sealed class CredentialsDialog
    {
        private readonly ICredUiApi _api;

        private string _captionValue;

        private string _messageValue;

        private string _name = string.Empty;

        private SecureString _password = null;

        /// <summary>
        ///     Gets or sets if the save checkbox status.
        /// </summary>
        private bool _saveChecked;

        /// <summary>
        ///     Gets the state of the Save check box when the dialog was dismissed.
        /// </summary>
        /// <remarks>
        ///     Only meaningful when the dialog was shown with <c>showSaveCheckbox: true</c>; the
        ///     underlying API ignores and does not populate this value otherwise.
        /// </remarks>
        public bool SaveChecked => _saveChecked;

        /// <summary>
        ///     Gets or sets the password for the credentials.
        /// </summary>
        public SecureString Password
        {
            get
            {
                return _password;
            }
            set
            {
                if (value != null)
                {
                    if (value.Length > CREDUI.MAX_PASSWORD_LENGTH)
                    {
                        var message = string.Format(
                            CultureInfo.InvariantCulture,
                            "The password has a maximum length of {0} characters.",
                            CREDUI.MAX_PASSWORD_LENGTH);
                        throw new ArgumentException(message, "Password");
                    }
                }
                // Convert to secure string here
                _password = value;
            }
        }

        /// <summary>
        ///     Gets or sets the username for the credentials.
        /// </summary>
        public string UserName
        {
            get
            {
                return _name;
            }
            set
            {
                if (value != null)
                {
                    if (value.Length > CREDUI.MAX_USERNAME_LENGTH)
                    {
                        var message = string.Format(
                            CultureInfo.InvariantCulture,
                            "The username has a maximum length of {0} characters.",
                            CREDUI.MAX_USERNAME_LENGTH);
                        throw new ArgumentException(message, "UserName");
                    }
                }
                _name = value;
            }
        }

        /// <summary>
        ///     Gets or sets the caption of the dialog.
        /// </summary>
        /// <remarks> A null value will cause a system default caption to be used. </remarks>
        private string Message
        {
            get
            {
                return _messageValue;
            }
            set
            {
                if (value != null)
                {
                    if (value.Length > CREDUI.MAX_MESSAGE_LENGTH)
                    {
                        var message = string.Format(
                            CultureInfo.InvariantCulture,
                            "The caption has a maximum length of {0} characters.",
                            CREDUI.MAX_MESSAGE_LENGTH);
                        throw new ArgumentException(message, "Message");
                    }
                }
                _messageValue = value;
            }
        }

        /// <summary>
        ///     Gets or sets the caption of the dialog.
        /// </summary>
        /// <remarks> A null value will cause a default caption to be used. </remarks>
        private string Caption
        {
            get
            {
                return _captionValue;
            }
            set
            {
                if (value != null)
                {
                    if (value.Length > CREDUI.MAX_CAPTION_LENGTH)
                    {
                        var caption = string.Format(
                            CultureInfo.InvariantCulture,
                            "The caption has a maximum length of {0} characters.",
                            CREDUI.MAX_CAPTION_LENGTH);
                        throw new ArgumentException(caption, "Caption");
                    }
                }
                _captionValue = value;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="T:CredUICredential.CredentialsDialog"
        ///     /> class with the specified caption.
        /// </summary>
        /// <param name="message">
        ///     The caption of the dialog (null will cause a system default caption to be used).
        /// </param>
        public CredentialsDialog(string caption = "", string message = "")
            : this(CredUiApi.Instance, caption, message)
        {
        }

        /// <summary>
        ///     Initializes a new instance that talks to the supplied view of <c>credui.dll</c>.
        /// </summary>
        /// <remarks>
        ///     The dialog is modal and interactive, so tests substitute the native layer here in
        ///     order to drive the logic that surrounds it.
        /// </remarks>
        internal CredentialsDialog(ICredUiApi api, string caption = "", string message = "")
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));

            if (string.IsNullOrEmpty(caption))
            {
                Caption = "Credentials";
            }
            else
            {
                Caption = caption;
            }

            if (string.IsNullOrEmpty(message))
            {
                Message = "Enter your credentials.";
            }
            else
            {
                Message = message;
            }
            _saveChecked = false;
        }

        /// <summary>
        ///     Shows the credentials dialog with the specified owner, username, password and save
        ///     checkbox status.
        /// </summary>
        /// <param name="username"> The username for the credentials. </param>
        /// <param name="showSaveCheckbox">
        ///     Whether the dialog should display the Save check box. See <see cref="SaveChecked"/>
        ///     for the checkbox state after the dialog is dismissed.
        /// </param>
        /// <returns> Returns a DialogResult indicating the user action. </returns>
        public DialogResult Show(string username = "", bool showSaveCheckbox = false)
        {
            if (string.IsNullOrEmpty(username))
            {
                username = "";
            }
            UserName = username;
            _saveChecked = false;

            return ShowDialog(GetOwnerHandle(), showSaveCheckbox);
        }

        /// <summary>
        ///     The window the dialog should sit in front of.
        /// </summary>
        /// <remarks>
        ///     A console host normally has no main window, in which case this is
        ///     <see cref="IntPtr.Zero"/> and Windows centres the dialog on the desktop - which is
        ///     what makes the prompt reachable from a script running in the background.
        /// </remarks>
        private static IntPtr GetOwnerHandle()
        {
            using var process = Process.GetCurrentProcess();
            return process.MainWindowHandle;
        }


        /// <summary>
        ///     Returns a DialogResult from the specified code.
        /// </summary>
        /// <param name="code"> The credential return code. </param>
        /// <remarks>
        ///     Anything that is neither success nor a cancellation is reported as the Win32 error
        ///     it is. <see cref="Win32Exception"/> carries the numeric code through for callers
        ///     that want to branch on it, and looks the description up from the operating system,
        ///     which knows about far more errors than this module could usefully enumerate.
        /// </remarks>
        private static DialogResult GetDialogResult(CREDUI.ReturnCodes code)
        {
            switch (code)
            {
                case CREDUI.ReturnCodes.NO_ERROR:
                    return DialogResult.OK;

                case CREDUI.ReturnCodes.ERROR_CANCELLED:
                    return DialogResult.Cancel;

                default:
                    throw new Win32Exception((int)code);
            }
        }

        /// <summary>
        ///     Returns the flags for dialog display options.
        /// </summary>
        /// <param name="showSaveCheckbox"> Whether to include the Save check box in the dialog. </param>
        private static CREDUI.FLAGS GetFlags(bool showSaveCheckbox)
        {
            var flags = CREDUI.FLAGS.CREDUIWIN_AUTHPACKAGE_ONLY;
            if (showSaveCheckbox)
            {
                flags |= CREDUI.FLAGS.CREDUIWIN_CHECKBOX;
            }
            return flags;
        }

        /// <summary>
        ///     Returns the info structure for dialog display settings.
        /// </summary>
        /// <param name="owner">
        ///     Handle of the window the dialog will display in front of.
        /// </param>
        private CREDUI.INFO GetInfo(IntPtr owner)
        {
            var info = new CREDUI.INFO();
            info.hwndParent = owner;
            info.pszCaptionText = Caption;
            info.pszMessageText = Message;
            info.cbSize = Marshal.SizeOf(info);
            return info;
        }

        /// <summary>
        ///     Records what the user actually typed.
        /// </summary>
        /// <remarks>
        ///     This deliberately writes the fields rather than the properties. The property
        ///     setters guard against a <em>caller</em> supplying something Windows will not
        ///     accept; what comes back out of the dialog is Windows' own answer, and rejecting it
        ///     here would turn a successful prompt into an exception.
        /// </remarks>
        private void SetCredentials(StringBuilder n, StringBuilder domain, StringBuilder pw)
        {
            _name = Qualify(n.ToString(), domain.ToString());
            _password = Plaintext.ToSecureString(pw);
        }

        /// <summary>
        ///     Puts the user name back together from the two buffers Windows fills in.
        /// </summary>
        /// <remarks>
        ///     <c>CredUnPackAuthenticationBuffer</c> may report the domain separately from the user
        ///     name, and dropping it changes which account the credential is for. It usually leaves
        ///     the domain empty and returns whatever the user typed verbatim, so there is nothing
        ///     to do; when it does not, and the user name is not already qualified by a domain
        ///     prefix or a user principal name suffix, the two halves belong back together.
        /// </remarks>
        private static string Qualify(string userName, string domain)
        {
            if (string.IsNullOrEmpty(domain)
                || string.IsNullOrEmpty(userName)
                || userName.Contains('\\')
                || userName.Contains('@'))
            {
                return userName;
            }

            return domain + "\\" + userName;
        }

        /// <summary>
        ///     Decodes the buffer the dialog produced, growing the destination buffers if Windows
        ///     says they are too small.
        /// </summary>
        /// <remarks>
        ///     <c>CredUnPackAuthenticationBuffer</c> fails with <c>ERROR_INSUFFICIENT_BUFFER</c>
        ///     and writes the sizes it needs into the capacity arguments, so one more attempt with
        ///     those sizes is always enough.
        /// </remarks>
        /// <returns> <see langword="true"/> if the credential was decoded. </returns>
        private bool TryReadCredential(IntPtr authBuffer, uint authBufferSize, out int lastError)
        {
            var userNameCapacity = CREDUI.MAX_USERNAME_LENGTH;
            var domainCapacity = CREDUI.MAX_DOMAIN_TARGET_LENGTH;
            var passwordCapacity = CREDUI.MAX_PASSWORD_LENGTH;

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var userName = new StringBuilder(userNameCapacity);
                var domain = new StringBuilder(domainCapacity);
                var password = new StringBuilder(passwordCapacity);

                bool unpacked;
                try
                {
                    unpacked = _api.TryUnpackAuthenticationBuffer(
                        authBuffer, authBufferSize,
                        userName, ref userNameCapacity,
                        domain, ref domainCapacity,
                        password, ref passwordCapacity,
                        out lastError);

                    if (unpacked)
                    {
                        SetCredentials(userName, domain, password);
                    }
                }
                finally
                {
                    // The password is out of the buffer now, one way or the other. A failed first
                    // attempt can still have left part of it behind.
                    Plaintext.Overwrite(password);
                }

                if (unpacked)
                {
                    return true;
                }

                if (lastError != (int)CREDUI.ReturnCodes.ERROR_INSUFFICIENT_BUFFER)
                {
                    return false;
                }

                // Windows has just told us how much room it wants. Round we go again.
            }

            lastError = (int)CREDUI.ReturnCodes.ERROR_INSUFFICIENT_BUFFER;
            return false;
        }

        /// <summary>
        ///     Returns a DialogResult indicating the user action.
        /// </summary>
        /// <param name="owner">
        ///     Handle of the window the dialog will display in front of.
        /// </param>
        /// <param name="showSaveCheckbox"> Whether to include the Save check box in the dialog. </param>
        /// <remarks>
        ///     Sets the username, password and SaveChecked accessors to the state of the dialog as
        ///     it was dismissed by the user.
        /// </remarks>
        private DialogResult ShowDialog(IntPtr owner, bool showSaveCheckbox)
        {
            // set the API call parameters
            var info = GetInfo(owner);
            // make the API call
            uint authPackage = 0;
            var flags = GetFlags(showSaveCheckbox);
            var code = _api.PromptForWindowsCredentials(
                ref info,
                ref authPackage,
                out var outCredBuffer,
                out var outCredSize,
                ref _saveChecked,
                flags);

            if (code == CREDUI.ReturnCodes.NO_ERROR)
            {
                bool read;
                int readError;
                try
                {
                    read = TryReadCredential(outCredBuffer, outCredSize, out readError);
                }
                finally
                {
                    // The buffer belongs to us whether or not we could make sense of it, and it is
                    // holding the password in the clear until it is released.
                    _api.FreeAuthenticationBuffer(outCredBuffer, outCredSize);
                }

                if (!read)
                {
                    // Reporting success here would hand back a null password, and the caller would
                    // only find out about it somewhere else entirely.
                    throw new Win32Exception(
                        readError,
                        "The credential dialog returned a credential that could not be read.");
                }
            }
            return GetDialogResult(code);
        }
    }
}
