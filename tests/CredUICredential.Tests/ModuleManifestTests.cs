using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     The manifest is what the PowerShell Gallery publishes and what Import-Module reads. It
    ///     describes the assembly from the outside, and nothing in the build makes the two agree -
    ///     so when they drift, the first person to find out is whoever installed the module.
    /// </summary>
    public class ModuleManifestTests : IDisposable
    {
        private readonly PowerShellHost _host = new();
        private readonly Hashtable _manifest;

        public ModuleManifestTests()
        {
            var path = Repository.File("CredUICredential.psd1").FullName.Replace("'", "''");
            _manifest = (Hashtable)_host
                .Run($"Import-PowerShellDataFile -LiteralPath '{path}'")
                .Single()
                .BaseObject;
        }

        public void Dispose() => _host.Dispose();

        private string Entry(string key) => (string)_manifest[key];

        /// <summary>The cmdlets the assembly actually defines, named the way PowerShell names them.</summary>
        private static IEnumerable<string> DefinedCmdlets()
            => typeof(GetCredUICredentialCmdlet).Assembly
                .GetExportedTypes()
                .Select(type => new
                {
                    Type = type,
                    Attribute = type.GetCustomAttribute<CmdletAttribute>(inherit: false),
                })
                .Where(candidate => candidate.Attribute != null)
                .Select(candidate => $"{candidate.Attribute.VerbName}-{candidate.Attribute.NounName}")
                .OrderBy(name => name, StringComparer.Ordinal);

        [Fact]
        public void EveryCmdletTheAssemblyDefinesIsExported()
        {
            var exported = ((object[])_manifest["CmdletsToExport"])
                .Cast<string>()
                .OrderBy(name => name, StringComparer.Ordinal);

            Assert.Equal(DefinedCmdlets(), exported);
        }

        [Fact]
        public void NoFunctionsOrAliasesArePromisedThatTheAssemblyCannotProvide()
        {
            // A binary module has no script functions to export, and the manifest claiming
            // otherwise makes Import-Module go looking for things that do not exist.
            Assert.Empty((object[])_manifest["FunctionsToExport"]);
            Assert.Empty((object[])_manifest["AliasesToExport"]);
        }

        [Fact]
        public void TheManifestVersionMatchesTheAssemblyItShips()
        {
            // The Gallery shows the manifest's version and Get-Module reports it, but anything
            // reading the assembly - a binding redirect, a support request quoting a stack trace -
            // sees the other one.
            var assemblyVersion = typeof(GetCredUICredentialCmdlet).Assembly.GetName().Version;

            Assert.Equal(assemblyVersion, Version.Parse(Entry("ModuleVersion")));
        }

        [Fact]
        public void TheRootModulePointsAtWhatTheBuildActuallyProduces()
        {
            var project = XDocument.Load(Repository.File("CredUICredential/CredUICredential.csproj").FullName);
            var targetFramework = project.Descendants("TargetFramework").Single().Value;
            var assemblyName = typeof(GetCredUICredentialCmdlet).Assembly.GetName().Name;

            Assert.Equal(
                $@".\CredUICredential\bin\Release\{targetFramework}\{assemblyName}.dll",
                Entry("RootModule"));
        }

        [Fact]
        public void TheRootModuleIsWhereTheManifestSaysItIs()
        {
            // Local Import-Module and ModuleManifestTests read this path. New-GalleryPackage.ps1
            // rewrites RootModule in the staged copy; if the in-repo DLL is missing, that stage
            // has nothing to copy and the Gallery package would be empty.
            Repository.File(Entry("RootModule").TrimStart('.', '\\'));
        }

        [Fact]
        public void TheDeclaredPowerShellVersionCanActuallyLoadTheAssembly()
        {
            // A binary module can only be loaded by a host running at least the .NET version it
            // was built for. Understating this does not make the module work on older hosts; it
            // just replaces a clear "requires PowerShell x.y" with an assembly load failure.
            var targetFramework = typeof(GetCredUICredentialCmdlet).Assembly
                .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
                .FrameworkName;
            var dotnetMajor = int.Parse(
                targetFramework.Split("Version=v")[1].Split('.')[0],
                System.Globalization.CultureInfo.InvariantCulture);
            var minimumHost = PowerShellVersionFor(dotnetMajor);

            Assert.True(
                Version.Parse(Entry("PowerShellVersion")) >= minimumHost,
                $"The module is built for .NET {dotnetMajor}, which needs PowerShell "
                + $"{minimumHost} or later, but the manifest asks for {Entry("PowerShellVersion")}.");
        }

        /// <summary>The first PowerShell release built on a given .NET major version.</summary>
        private static Version PowerShellVersionFor(int dotnetMajor) => dotnetMajor switch
        {
            <= 6 => new Version(7, 2),
            7 => new Version(7, 3),
            8 => new Version(7, 4),
            9 => new Version(7, 5),
            10 => new Version(7, 6),
            _ => throw new NotSupportedException(
                $".NET {dotnetMajor} postdates this table; add the PowerShell release built on it."),
        };

        [Fact]
        public void TheHelpUriAndProjectUriAgreeOnWhereTheProjectLives()
        {
            var privateData = (Hashtable)((Hashtable)_manifest["PrivateData"])["PSData"];
            var projectUri = (string)privateData["ProjectUri"];

            Assert.StartsWith(projectUri, Entry("HelpInfoURI"), StringComparison.Ordinal);
        }
    }
}
