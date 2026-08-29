using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CredUICredential.Pinvoke
{
    /// <summary>
    ///     The slice of <c>secur32.dll</c> used to resolve authentication-package names to the
    ///     ids <c>CredUIPromptForWindowsCredentials</c> reports in <c>pulAuthPackage</c>.
    /// </summary>
    internal static class SECUR32
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct LSA_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [DllImport("secur32.dll", CallingConvention = CallingConvention.Winapi)]
        internal static extern int LsaConnectUntrusted(out IntPtr LsaHandle);

        [DllImport("secur32.dll", CallingConvention = CallingConvention.Winapi)]
        internal static extern int LsaLookupAuthenticationPackage(
            IntPtr LsaHandle,
            ref LSA_STRING PackageName,
            out uint AuthenticationPackage);

        [DllImport("secur32.dll", CallingConvention = CallingConvention.Winapi)]
        internal static extern int LsaDeregisterLogonProcess(IntPtr LsaHandle);

        /// <summary>
        ///     Looks up an authentication package by its ANSI name. Returns <see langword="false"/>
        ///     when LSA does not know the name; does not throw.
        /// </summary>
        internal static bool TryLookupPackage(IntPtr lsaHandle, string name, out uint package)
        {
            package = 0;
            var bytes = Encoding.ASCII.GetBytes(name);
            var buffer = Marshal.AllocHGlobal(bytes.Length + 1);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                Marshal.WriteByte(buffer, bytes.Length, 0);
                var packageName = new LSA_STRING
                {
                    Length = (ushort)bytes.Length,
                    MaximumLength = (ushort)(bytes.Length + 1),
                    Buffer = buffer,
                };
                return LsaLookupAuthenticationPackage(lsaHandle, ref packageName, out package) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
