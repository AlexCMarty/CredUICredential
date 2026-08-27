using System;
using System.ComponentModel;
using System.Management.Automation;
using System.Windows.Forms;

namespace CredUICredential
{
    [Cmdlet(VerbsCommon.Get, "CredUICredential", DefaultParameterSetName = credentialSet, HelpUri = "https://github.com/AlexCMarty/CredUICredential/blob/master/CredUICredential.md")]
    [OutputType(typeof(PSCredential), ParameterSetName = new string[] { credentialSet, messageSet })]
    [OutputType(typeof(PSObject), ParameterSetName = new string[] { messageSet })]

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
        [ValidateNotNullOrEmpty]
        public string Message { get; set; }

        /// <summary>
        /// Gets and sets the user supplied title providing description about which script/function is
        /// requesting the PSCredential from the user.
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = messageSet)]
        [ValidateNotNullOrEmpty]
        public string Title { get; set; }

        /// <summary>
        /// Gets and sets the user supplied username to be used while creating the PSCredential.
        /// </summary>
        [Parameter(Position = 0, Mandatory = false, ParameterSetName = messageSet)]
        [ValidateNotNullOrEmpty()]
        public string UserName { get; set; }

        /// <summary>
        /// Gets and sets whether the dialog displays a Save check box. When specified, the
        /// cmdlet outputs an object with Credential and Checkbox properties instead of a bare
        /// PSCredential.
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = messageSet)]
        public SwitchParameter ShowSaveCheckbox { get; set; }

        /// <summary>
        /// The Credential parameter set name.
        /// </summary>
        private const string credentialSet = "CredentialSet";

        /// <summary>
        /// The Message parameter set name.
        /// </summary>
        private const string messageSet = "MessageSet";

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
                var dialog = CreateDialog();
                var dialogResult = dialog.Show(UserName, ShowSaveCheckbox.IsPresent);
                if (dialogResult == DialogResult.OK)
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

            if (Credential != null)
            {
                WriteObject(Credential);
            }
        }
    }
}
