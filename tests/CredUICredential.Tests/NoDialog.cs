using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Sdk;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Guards the code paths that must never raise the modal Windows credential dialog.
    ///     A regression that starts prompting would otherwise block the whole test run on a
    ///     dialog nobody is there to dismiss, so run those paths on a worker thread and fail
    ///     loudly if they do not come back.
    /// </summary>
    internal static class NoDialog
    {
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

        public static void Expected(Action body)
        {
            Expected<object>(() =>
            {
                body();
                return null;
            });
        }

        public static T Expected<T>(Func<T> body)
        {
            // A dedicated thread rather than the pool: if the body really is stuck behind a
            // dialog it will never return, and abandoning a pool thread hurts the rest of the run.
            T result = default;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = body();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            })
            { IsBackground = true };

            thread.Start();

            if (!thread.Join(Limit))
            {
                throw new XunitException(
                    $"The operation did not finish within {Limit.TotalSeconds:0} seconds. " +
                    "This path is expected to complete without showing the credential dialog.");
            }

            if (failure != null)
            {
                ExceptionDispatchInfoRethrow(failure);
            }

            return result;
        }

        private static void ExceptionDispatchInfoRethrow(Exception exception)
            => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
