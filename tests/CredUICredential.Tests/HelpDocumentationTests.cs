using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     The MAML file is what Get-Help prints. Update-Help.ps1 generates it from
    ///     CredUICredential.md; it is not committed. CI regenerates it before these tests and
    ///     ships it in the Gallery package. Locally, run the script before <c>dotnet test</c>
    ///     on a clean tree.
    /// </summary>
    /// <remarks>
    ///     These tests compare the generated file against the cmdlet type itself. They
    ///     deliberately say nothing about the prose.
    /// </remarks>
    public class HelpDocumentationTests
    {
        private static readonly XNamespace Maml = "http://schemas.microsoft.com/maml/2004/10";
        private static readonly XNamespace Command = "http://schemas.microsoft.com/maml/dev/command/2004/10";

        private static readonly XElement Help = LoadHelp();

        private static XElement LoadHelp()
        {
            FileInfo file;
            try
            {
                file = Repository.File("en-US/CredUICredential.dll-Help.xml");
            }
            catch (FileNotFoundException exception)
            {
                throw new InvalidOperationException(
                    "Generated help is missing. Run pwsh ./Update-Help.ps1 before the tests.",
                    exception);
            }

            return XDocument
                .Load(file.FullName)
                .Descendants(Command + "command")
                .Single();
        }

        private static readonly CmdletAttribute Cmdlet =
            typeof(GetCredUICredentialCmdlet).GetCustomAttribute<CmdletAttribute>();

        /// <summary>The cmdlet's own parameters, ignoring the ones PowerShell adds to everything.</summary>
        private static IEnumerable<PropertyInfo> DeclaredParameters()
            => typeof(GetCredUICredentialCmdlet)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.GetCustomAttributes<ParameterAttribute>().Any());

        private static IEnumerable<XElement> DocumentedParameters()
            => Help.Element(Command + "parameters").Elements(Command + "parameter");

        private static string NameOf(XElement parameter)
            => parameter.Element(Maml + "name").Value;

        [Fact]
        public void TheHelpIsWrittenForTheCmdletTheAssemblyDefines()
        {
            var details = Help.Element(Command + "details");

            Assert.Equal(Cmdlet.VerbName, details.Element(Command + "verb").Value);
            Assert.Equal(Cmdlet.NounName, details.Element(Command + "noun").Value);
            Assert.Equal(
                $"{Cmdlet.VerbName}-{Cmdlet.NounName}",
                details.Element(Command + "name").Value);
        }

        [Fact]
        public void EveryParameterIsDocumentedAndNothingElseIs()
        {
            var declared = DeclaredParameters()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal);

            var documented = DocumentedParameters()
                .Select(NameOf)
                .OrderBy(name => name, StringComparer.Ordinal);

            Assert.Equal(declared, documented);
        }

        [Fact]
        public void EveryDocumentedParameterHasSomethingToSayAboutItself()
        {
            foreach (var parameter in DocumentedParameters())
            {
                var description = parameter.Element(Maml + "description");
                Assert.True(
                    description is not null && description.Value.Trim().Length > 0,
                    $"The help entry for -{NameOf(parameter)} has no description.");
            }
        }

        [Fact]
        public void TheDocumentedSyntaxMatchesTheRealParameterSets()
        {
            var real = typeof(GetCredUICredentialCmdlet)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(
                    property => property.GetCustomAttributes<ParameterAttribute>(),
                    (property, attribute) => new { property.Name, attribute.ParameterSetName })
                .GroupBy(entry => entry.ParameterSetName)
                .Select(set => string.Join(",", set.Select(entry => entry.Name).OrderBy(name => name, StringComparer.Ordinal)))
                .OrderBy(set => set, StringComparer.Ordinal);

            var documented = Help
                .Element(Command + "syntax")
                .Elements(Command + "syntaxItem")
                .Select(item => string.Join(
                    ",",
                    item.Elements(Command + "parameter").Select(NameOf).OrderBy(name => name, StringComparer.Ordinal)))
                .OrderBy(set => set, StringComparer.Ordinal);

            Assert.Equal(real, documented);
        }

        [Theory]
        [InlineData("required", nameof(ParameterAttribute.Mandatory))]
        [InlineData("pipelineInput", nameof(ParameterAttribute.ValueFromPipeline))]
        public void TheDocumentedBindingRulesMatchTheOnesPowerShellEnforces(string attributeName, string parameterProperty)
        {
            // Documenting a parameter as pipeline-bindable when it is not sends people to write
            // "$c | Get-CredUICredential" and watch it fail to bind.
            var expectations = DeclaredParameters().ToDictionary(
                property => property.Name,
                property => property
                    .GetCustomAttributes<ParameterAttribute>()
                    .Any(attribute => (bool)typeof(ParameterAttribute)
                        .GetProperty(parameterProperty)
                        .GetValue(attribute)));

            foreach (var parameter in DocumentedParameters())
            {
                var name = NameOf(parameter);
                Assert.Equal(
                    expectations[name].ToString().ToLowerInvariant(),
                    parameter.Attribute(attributeName).Value);
            }
        }

        [Fact]
        public void TheHelpUriOnTheCmdletPointsAtDocumentationThatExists()
        {
            // Get-Help -Online opens this, and the file it names lives in the repository.
            Assert.EndsWith("/CredUICredential.md", Cmdlet.HelpUri, StringComparison.Ordinal);
            Repository.File("CredUICredential.md");
        }
    }
}
