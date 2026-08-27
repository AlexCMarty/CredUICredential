using System;
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
        public CredentialsDialog(string caption="", string message = "")
        {
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

            // Get the owner
            var owner = new NativeWindow();
            owner.AssignHandle(Process.GetCurrentProcess().MainWindowHandle);

            return ShowDialog(owner, showSaveCheckbox);
        }

        private static SecureString ConvertToSecureString(string value)
        {
            var secureString = new SecureString();
            foreach (var c in value)
            {
                secureString.AppendChar(c);
            }
            return secureString;
        }
        /// <summary>
        ///     Returns a DialogResult from the specified code.
        /// </summary>
        /// <param name="code"> The credential return code. </param>
        private static DialogResult GetDialogResult(CREDUI.ReturnCodes code)
        {
            switch (code)
            {
                case CREDUI.ReturnCodes.NO_ERROR:
                    return DialogResult.OK;

                case CREDUI.ReturnCodes.ERROR_CANCELLED:
                    return DialogResult.Cancel;

                case CREDUI.ReturnCodes.ERROR_NO_SUCH_LOGON_SESSION:
                    throw new ApplicationException("No such logon session.");
                case CREDUI.ReturnCodes.ERROR_NOT_FOUND:
                    throw new ApplicationException("Not found.");
                case CREDUI.ReturnCodes.ERROR_INVALID_ACCOUNT_NAME:
                    throw new ApplicationException("Invalid account username.");
                case CREDUI.ReturnCodes.ERROR_INSUFFICIENT_BUFFER:
                    throw new ApplicationException("Insufficient buffer.");
                case CREDUI.ReturnCodes.ERROR_INVALID_PARAMETER:
                    throw new ApplicationException("Invalid parameter.");
                case CREDUI.ReturnCodes.ERROR_INVALID_FLAGS:
                    throw new ApplicationException("Invalid flags.");
                default:
                    throw new ApplicationException("Unknown credential result encountered.");
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
        ///     The System.Windows.Forms.IWin32Window the dialog will display in front of.
        /// </param>
        private CREDUI.INFO GetInfo(IWin32Window owner)
        {
            var info = new CREDUI.INFO();
            if (owner != null) info.hwndParent = owner.Handle;
            info.pszCaptionText = Caption;
            info.pszMessageText = Message;
            info.cbSize = Marshal.SizeOf(info);
            return info;
        }

        private void SetCredentials(StringBuilder n, StringBuilder pw)
        {
            UserName = n.ToString();
            Password = ConvertToSecureString(pw.ToString());
        }

        /// <summary>
        ///     Returns a DialogResult indicating the user action.
        /// </summary>
        /// <param name="owner">
        ///     The System.Windows.Forms.IWin32Window the dialog will display in front of.
        /// </param>
        /// <param name="showSaveCheckbox"> Whether to include the Save check box in the dialog. </param>
        /// <remarks>
        ///     Sets the username, password and SaveChecked accessors to the state of the dialog as
        ///     it was dismissed by the user.
        /// </remarks>
        private DialogResult ShowDialog(IWin32Window owner, bool showSaveCheckbox)
        {
            // set the API call parameters
            var name = new StringBuilder(CREDUI.MAX_USERNAME_LENGTH);
            name.Append(UserName);
            var password = new StringBuilder(CREDUI.MAX_PASSWORD_LENGTH);
            var info = GetInfo(owner);
            // make the API call
            uint authPackage = 0;
            var flags = GetFlags(showSaveCheckbox);
            var code = CREDUI.CredUIPromptForWindowsCredentials(ref info,
                0,
                ref authPackage,
                IntPtr.Zero,
                0,
                out var outCredBuffer,
                out var outCredSize,
                ref _saveChecked,
                flags);

            if (code == CREDUI.ReturnCodes.NO_ERROR)
            {
                var domainBuf = new StringBuilder(100);
                var maxUserName = CREDUI.MAX_USERNAME_LENGTH;
                var maxDomain = CREDUI.MAX_DOMAIN_TARGET_LENGTH;
                var maxPassword = CREDUI.MAX_PASSWORD_LENGTH;
                if (CREDUI.CredUnPackAuthenticationBuffer(1, outCredBuffer, outCredSize, name, ref maxUserName,
                        domainBuf, ref maxDomain, password, ref maxPassword))
                {
                    //clear the memory allocated by CredUIPromptForWindowsCredentials
                    CREDUI.CoTaskMemFree(outCredBuffer);
                    SetCredentials(name, password);
                }
            }
            return GetDialogResult(code);
        }
    }
}