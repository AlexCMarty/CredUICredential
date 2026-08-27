using System;
using System.Linq;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     <c>Get-CredUICredential</c> is advertised as a drop-in replacement for the built-in
    ///     <c>Get-Credential</c>. These tests compare the two command surfaces directly, so the
    ///     built-in cmdlet — not a hand-copied table — is the thing that says what parity means.
    /// </summary>
    public class DropInParityTests : IDisposable
    {
        private const string DescribeParameters = @"
            param($CommandName, $Names)
            (Get-Command $CommandName).Parameters.Values |
                Where-Object { $_.Name -in $Names } |
                ForEach-Object {
                    $p = $_
                    $p.Attributes |
                        Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] } |
                        ForEach-Object {
                            '{0}|set={1}|mandatory={2}|position={3}|pipeline={4}|pipelineByName={5}' -f
                                $p.Name, $_.ParameterSetName, $_.Mandatory, $_.Position,
                                $_.ValueFromPipeline, $_.ValueFromPipelineByPropertyName
                        }
                } | Sort-Object";

        private static readonly string[] SharedParameters =
            { "Credential", "Message", "Title", "UserName" };

        private readonly PowerShellHost _host = new();

        public void Dispose() => _host.Dispose();

        private string[] Describe(string commandName)
            => _host.Run(
                    $"& {{ {DescribeParameters} }} -CommandName '{commandName}' -Names @({string.Join(",", SharedParameters.Select(n => $"'{n}'"))})")
                .Select(o => (string)o.BaseObject)
                .ToArray();

        [Fact]
        public void SharedParametersBindExactlyLikeGetCredential()
        {
            var builtIn = Describe("Get-Credential");
            var ours = Describe("Get-CredUICredential");

            Assert.NotEmpty(builtIn);
            Assert.Equal(builtIn, ours);
        }

        [Fact]
        public void DefaultParameterSetMatchesGetCredential()
        {
            var builtIn = _host.Run("(Get-Command Get-Credential).DefaultParameterSet").Single().BaseObject;
            var ours = _host.Run("(Get-Command Get-CredUICredential).DefaultParameterSet").Single().BaseObject;

            Assert.Equal(builtIn, ours);
        }

        [Fact]
        public void ShowSaveCheckboxIsTheOnlyParameterAddedOnTopOfGetCredential()
        {
            var extra = _host.Run(@"
                $mine = (Get-Command Get-CredUICredential).Parameters.Keys
                $theirs = (Get-Command Get-Credential).Parameters.Keys
                $mine | Where-Object { $_ -notin $theirs } | Sort-Object")
                .Select(o => (string)o.BaseObject)
                .ToArray();

            Assert.Equal(new[] { "ShowSaveCheckbox" }, extra);
        }

        [Fact]
        public void ShowSaveCheckboxBelongsToTheDialogParameterSet()
        {
            var sets = _host.Run(@"
                (Get-Command Get-CredUICredential).ParameterSets |
                    Where-Object { 'ShowSaveCheckbox' -in $_.Parameters.Name } |
                    ForEach-Object Name")
                .Select(o => (string)o.BaseObject)
                .ToArray();

            Assert.Equal(new[] { "MessageSet" }, sets);
        }
    }
}
