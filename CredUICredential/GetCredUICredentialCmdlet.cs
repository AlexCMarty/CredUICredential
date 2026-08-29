using System;
using System.ComponentModel;
using System.Management.Automation;
using System.Windows.Forms;
using CredUICredential.Pinvoke;

namespace CredUICredential
{
    [Cmdlet(VerbsCommon.Get, "CredUICredential", DefaultParameterSetName = credentialSet, HelpUri = "https://github.com/AlexCMarty/CredUICredential/blob/master/CredUICredential.md")]
    [OutputType(typeof(PSCredential), ParameterSetName = new string[] { credentialSet, messageSet, retryNormalUserSet, retryAdminUserSet })]
    [OutputType(typeof(PSObject), ParameterSetName = new string[] { messageSet, retryNormalUserSet, retryAdminUserSet })]
    public class GetCredUICredentialCmdlet : PSCmdlet
    {
        /// <summary>
        /// Gets or sets the underlying PSCredential of
        /// the instance.
        /// </summary>
        [Parameter(Position = 0, ParameterSetName = credentialSet)]
        [ValidateNotNull]
        [Credential()]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Gets and sets the user supplied message providing description about which script/function is
        /// requesting the PSCredential from the user.
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = messageSet)]
        [Parameter(Mandatory = false, ParameterSetName = retryNormalUserSet)]
        [Parameter(Mandatory = false, ParameterSetName = retryAdminUserSet)]
        [ValidateNotNullOrEmpty]
        public string Message { get; set; }

        /// <summary>
        /// Gets and sets the user supplied title providing description about which script/function is
        /// requesting the PSCredential from the user.
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = messageSet)]
        [Parameter(Mandatory = false, ParameterSetName = retryNormalUserSet)]
        [Parameter(Mandatory = false, ParameterSetName = retryAdminUserSet)]
        [ValidateNotNullOrEmpty]
        public string Title { get; set; }

        /// <summary>
        /// Gets and sets the user supplied username to be used while creating the PSCredential.
        /// </summary>
        [Parameter(Position = 0, Mandatory = false, ParameterSetName = messageSet)]
        [Parameter(Position = 0, Mandatory = false, ParameterSetName = retryNormalUserSet)]
        [Parameter(Position = 0, Mandatory = false, ParameterSetName = retryAdminUserSet)]
        [ValidateNotNullOrEmpty()]
        public string UserName { get; set; }

        /// <summary>
        /// Gets and sets whether the dialog displays a Save check box. When specified, the
        /// cmdlet outputs an object with Credential and Checkbox properties instead of a bare
        /// PSCredential.
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = messageSet)]
        [Parameter(Mandatory = false, ParameterSetName = retryNormalUserSet)]
        [Parameter(Mandatory = false, ParameterSetName = retryAdminUserSet)]
        public SwitchParameter ShowSaveCheckbox { get; set; }

        /// <summary>
        /// Gets and sets whether the dialog is shown again until the password logs on, or the
        /// user cancels, or <see cref="MaxAttempts"/> is exhausted.
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = retryNormalUserSet)]
        public SwitchParameter RetryNormalUser { get; set; }

        /// <summary>
        /// Gets and sets whether the dialog is shown again until the password logs on as a
        /// local administrator, or the user cancels, or <see cref="MaxAttempts"/> is exhausted.
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = retryAdminUserSet)]
        public SwitchParameter RetryAdminUser { get; set; }

        /// <summary>
        /// Gets and sets how many times the user may submit the dialog when a retry switch is
        /// used. The range is 1 to 10; the default is 3.
        /// </summary>
        [Parameter(ParameterSetName = retryNormalUserSet)]
        [Parameter(ParameterSetName = retryAdminUserSet)]
        [ValidateRange(1, 10)]
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// The Credential parameter set name.
        /// </summary>
        private const string credentialSet = "CredentialSet";

        /// <summary>
        /// The Message parameter set name.
        /// </summary>
        private const string messageSet = "MessageSet";

        /// <summary>
        /// The RetryNormalUser parameter set name.
        /// </summary>
        private const string retryNormalUserSet = "RetryNormalUserSet";

        /// <summary>
        /// The RetryAdminUser parameter set name.
        /// </summary>
        private const string retryAdminUserSet = "RetryAdminUserSet";

        /// <summary>
        /// Creates the dialog this cmdlet prompts with.
        /// </summary>
        /// <remarks>
        /// The prompt is modal and interactive, so this is the one point where it can be
        /// substituted in order to exercise everything the cmdlet does around it.
        /// </remarks>
        internal virtual CredentialsDialog CreateDialog()
            => new(caption: Title, message: Message);

        /// <summary>
        /// Creates the logon check used when a retry switch is set.
        /// </summary>
        internal virtual ILogonApi CreateLogonApi()
            => LogonApi.Instance;

        /// <summary>
        /// The command outputs the stored PSCredential.
        /// </summary>
        protected override void BeginProcessing()
        {
            if (Credential != null)
            {
                WriteObject(Credential);
                return;
            }

            try
            {
                if (RetryNormalUser.IsPresent || RetryAdminUser.IsPresent)
                {
                    PromptUntilValid();
                    return;
                }

                var dialog = CreateDialog();
                var dialogResult = dialog.Show(UserName, ShowSaveCheckbox.IsPresent);
                if (dialogResult == DialogResult.OK)
                {
                    if (dialog.MessageType != KERB.InteractiveLogon)
                    {
                        WriteNonPasswordError(dialog.UserName);
                        return;
                    }

                    WriteCredential(dialog);
                }
            }
            // A prompt that Windows refuses to show, or a credential it will not hand back, is a
            // failure of this one command - not a reason to tear down the caller's pipeline.
            catch (Exception exception) when (exception is ArgumentException or Win32Exception)
            {
                ErrorRecord errorRecord = new(
                    exception,
                    "CouldNotPromptForCredential",
                    ErrorCategory.InvalidOperation,
                    targetObject: null);
                WriteError(errorRecord);
            }
        }

        /// <summary>
        ///     Shows the dialog until the password logs on (and is a local administrator, when
        ///     that was asked for), the user cancels, a non-retryable logon error, or
        ///     <see cref="MaxAttempts"/>.
        /// </summary>
        private void PromptUntilValid()
        {
            var dialog = CreateDialog();
            var logon = CreateLogonApi();
            var username = UserName;
            var authError = 0;
            var attempts = 0;

            while (true)
            {
                DialogResult dialogResult;
                try
                {
                    dialogResult = dialog.Show(username, ShowSaveCheckbox.IsPresent, authError);
                }
                catch (Exception exception) when (exception is ArgumentException or Win32Exception)
                {
                    ErrorRecord errorRecord = new(
                        exception,
                        "CouldNotPromptForCredential",
                        ErrorCategory.InvalidOperation,
                        targetObject: null);
                    WriteError(errorRecord);
                    return;
                }

                if (dialogResult != DialogResult.OK)
                {
                    return;
                }

                attempts++;
                if (dialog.MessageType != KERB.InteractiveLogon)
                {
                    if (attempts >= MaxAttempts)
                    {
                        WriteNonPasswordError(dialog.UserName);
                        return;
                    }

                    authError = ADVAPI.ERROR_LOGON_FAILURE;
                    username = dialog.UserName;
                    continue;
                }

                var result = logon.TryLogon(dialog.UserName, dialog.Password);

                if (result.Status == LogonStatus.Success)
                {
                    if (RetryNormalUser.IsPresent || result.IsLocalAdministrator)
                    {
                        WriteCredential(dialog);
                        return;
                    }

                    if (attempts >= MaxAttempts)
                    {
                        WriteError(new ErrorRecord(
                            new Win32Exception(ADVAPI.ERROR_ELEVATION_REQUIRED),
                            "CredentialNotAdministrator",
                            ErrorCategory.PermissionDenied,
                            targetObject: dialog.UserName));
                        return;
                    }

                    authError = ADVAPI.ERROR_ELEVATION_REQUIRED;
                    username = dialog.UserName;
                    continue;
                }

                if (result.Status == LogonStatus.NonRetryable)
                {
                    WriteError(new ErrorRecord(
                        new Win32Exception(result.NativeError),
                        "CredentialLogonFailed",
                        ErrorCategory.AuthenticationError,
                        targetObject: dialog.UserName));
                    return;
                }

                if (attempts >= MaxAttempts)
                {
                    WriteError(new ErrorRecord(
                        new Win32Exception(ADVAPI.ERROR_LOGON_FAILURE),
                        "CredentialValidationFailed",
                        ErrorCategory.AuthenticationError,
                        targetObject: dialog.UserName));
                    return;
                }

                authError = ADVAPI.ERROR_LOGON_FAILURE;
                username = dialog.UserName;
            }
        }

        private void WriteNonPasswordError(string userName)
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException(
                    "The credential dialog returned a credential that is not a username and password."),
                "CredentialNotPassword",
                ErrorCategory.InvalidData,
                targetObject: userName));
        }

        private void WriteCredential(CredentialsDialog dialog)
        {
            Credential = new PSCredential(dialog.UserName, dialog.Password);
            if (ShowSaveCheckbox.IsPresent)
            {
                var result = new PSObject();
                result.Properties.Add(new PSNoteProperty("Credential", Credential));
                result.Properties.Add(new PSNoteProperty("Checkbox", dialog.SaveChecked));
                WriteObject(result);
                return;
            }

            WriteObject(Credential);
        }
    }
}
