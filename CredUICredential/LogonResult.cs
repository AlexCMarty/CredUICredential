namespace CredUICredential
{
    internal enum LogonStatus
    {
        Success,
        LogonFailure,
        NonRetryable
    }

    /// <summary>
    ///     What a logon attempt decided about the password, and about local Administrators
    ///     membership when the password was accepted.
    /// </summary>
    internal readonly record struct LogonResult
    {
        public LogonStatus Status { get; init; }

        public int NativeError { get; init; }

        public bool IsLocalAdministrator { get; init; }

        public static LogonResult Succeeded(bool isLocalAdministrator)
            => new()
            {
                Status = LogonStatus.Success,
                NativeError = 0,
                IsLocalAdministrator = isLocalAdministrator
            };

        /// <summary>
        ///     Classifies a <c>LogonUser</c> failure. Only <c>ERROR_LOGON_FAILURE</c> is retryable.
        /// </summary>
        public static LogonResult Failed(int nativeError)
            => new()
            {
                Status = nativeError == Pinvoke.ADVAPI.ERROR_LOGON_FAILURE
                    ? LogonStatus.LogonFailure
                    : LogonStatus.NonRetryable,
                NativeError = nativeError,
                IsLocalAdministrator = false
            };
    }
}
