using System;
using System.Collections.Generic;
using System.Security;

namespace CredUICredential.Tests.Fakes
{
    /// <summary>
    ///     A scripted <see cref="ILogonApi"/>: each call dequeues a prepared outcome. Tests that
    ///     must not log on leave this unset; tests that retry enqueue the attempts they expect.
    /// </summary>
    internal sealed class ScriptedLogon : ILogonApi
    {
        private readonly Queue<LogonResult> _results = new();

        public List<(string UserName, int PasswordLength)> Calls { get; } = new();

        public ScriptedLogon(params LogonResult[] results)
        {
            foreach (var result in results)
            {
                _results.Enqueue(result);
            }
        }

        public LogonResult TryLogon(string userName, SecureString password)
        {
            Calls.Add((userName, password?.Length ?? 0));
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("ScriptedLogon has no remaining results.");
            }

            return _results.Dequeue();
        }
    }
}
