using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     <see cref="ScriptedDialogCmdlet"/> hands the fake API in through statics, because
    ///     PowerShell constructs the cmdlet. Tests that host that cmdlet must not run over
    ///     each other.
    /// </summary>
    [CollectionDefinition("ScriptedDialog")]
    public class ScriptedDialogCollection
    {
    }
}
