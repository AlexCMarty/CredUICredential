using System.Runtime.InteropServices;
using System.Text;

namespace CredUiSmoke;

/// <summary>
///     Turning what came back out of the prompt into something safe to print.
/// </summary>
internal static class Diagnostics
{
    /// <summary>
    ///     Every LSA package name worth asking about, so an unexpected <c>pulAuthPackage</c> can be
    ///     named rather than guessed at. The first three are the module's allow-list.
    /// </summary>
    internal static readonly string[] KnownPackageNames =
    {
        "Negotiate",
        "NTLM",
        "Kerberos",
        "MICROSOFT_AUTHENTICATION_PACKAGE_V1_0",
        "WDigest",
        "Schannel",
        "Microsoft Unified Security Protocol Provider",
        "CloudAP",
        "pku2u",
        "NegoExtender",
        "LiveSSP",
        "TSSSP",
        "WinNT",
        "Ngc",
        "NgcPack",
        "Passport",
    };

    internal static Dictionary<uint, List<string>> LookupPackages(out string? failure)
    {
        failure = null;
        var map = new Dictionary<uint, List<string>>();
        var status = Native.LsaConnectUntrusted(out var handle);
        if (status != 0)
        {
            failure = $"LsaConnectUntrusted failed with NTSTATUS 0x{status:X8}";
            return map;
        }

        try
        {
            foreach (var name in KnownPackageNames)
            {
                if (!TryLookup(handle, name, out var id))
                {
                    continue;
                }

                if (!map.TryGetValue(id, out var names))
                {
                    names = new List<string>();
                    map[id] = names;
                }

                names.Add(name);
            }
        }
        finally
        {
            Native.LsaDeregisterLogonProcess(handle);
        }

        return map;
    }

    private static bool TryLookup(IntPtr lsaHandle, string name, out uint package)
    {
        package = 0;
        var bytes = Encoding.ASCII.GetBytes(name);
        var buffer = Marshal.AllocHGlobal(bytes.Length + 1);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            Marshal.WriteByte(buffer, bytes.Length, 0);
            var packageName = new Native.LSA_STRING
            {
                Length = (ushort)bytes.Length,
                MaximumLength = (ushort)(bytes.Length + 1),
                Buffer = buffer,
            };
            return Native.LsaLookupAuthenticationPackage(lsaHandle, ref packageName, out package) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    ///     The <c>KERB_LOGON_SUBMIT_TYPE</c> a marshalled interactive-logon buffer starts with. A
    ///     buffer that does not start with one of these is not a password credential, whatever
    ///     <c>pulAuthPackage</c> said.
    /// </summary>
    internal static string DescribeMessageType(uint value) => value switch
    {
        2 => "KerbInteractiveLogon",
        6 => "KerbSmartCardLogon",
        7 => "KerbWorkstationUnlockLogon",
        8 => "KerbSmartCardUnlockLogon",
        9 => "KerbProxyLogon",
        10 => "KerbTicketLogon",
        11 => "KerbTicketUnlockLogon",
        12 => "KerbS4ULogon",
        13 => "KerbCertificateLogon",
        14 => "KerbCertificateS4ULogon",
        15 => "KerbCertificateUnlockLogon",
        16 => "KerbNoElevationLogon",
        17 => "KerbLuidLogon",
        _ => "unknown/not-a-KERB-submit-type",
    };

    /// <summary>
    ///     Reads the first DWORD of the output buffer. For a password submit this is the
    ///     <c>MessageType</c> of a marshalled <c>KERB_INTERACTIVE_UNLOCK_LOGON</c>; it is never
    ///     part of a secret, unlike the rest of the buffer, which is why nothing else is read.
    /// </summary>
    internal static uint? ReadLeadingDword(IntPtr buffer, uint size)
    {
        if (buffer == IntPtr.Zero || size < 4)
        {
            return null;
        }

        return unchecked((uint)Marshal.ReadInt32(buffer));
    }

    /// <summary>
    ///     What a decoded "password" is made of, without saying what it is. Enough to tell a real
    ///     password from UTF-16 mojibake, which is the whole point of the exercise.
    /// </summary>
    internal static string DescribeSecretShape(string? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        if (value.Length == 0)
        {
            return "length=0 (empty)";
        }

        int digits = 0, asciiLetters = 0, asciiPrintableOther = 0, controls = 0, nonAscii = 0, surrogates = 0, replacement = 0;
        foreach (var c in value)
        {
            if (char.IsSurrogate(c))
            {
                surrogates++;
            }
            else if (c == '\uFFFD')
            {
                replacement++;
            }
            else if (c < 0x20 || c == 0x7F)
            {
                controls++;
            }
            else if (c >= '0' && c <= '9')
            {
                digits++;
            }
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                asciiLetters++;
            }
            else if (c < 0x80)
            {
                asciiPrintableOther++;
            }
            else
            {
                nonAscii++;
            }
        }

        var looksLikeText = controls == 0 && nonAscii == 0 && surrogates == 0 && replacement == 0;
        return $"length={value.Length} digits={digits} asciiLetters={asciiLetters} asciiPunct={asciiPrintableOther} " +
               $"controls={controls} nonAscii={nonAscii} unpairedOrSurrogate={surrogates} U+FFFD={replacement} " +
               $"looksLikePlainText={looksLikeText}";
    }

    /// <summary>
    ///     Decodes the buffer the way the module does - two attempts, growing on
    ///     <c>ERROR_INSUFFICIENT_BUFFER</c> - and reports the shape of what came out.
    /// </summary>
    internal static string TryUnpack(IntPtr buffer, uint size, int unpackFlags)
    {
        var report = new StringBuilder();
        var userCapacity = Native.CREDUI_MAX_USERNAME_LENGTH;
        var domainCapacity = Native.CREDUI_MAX_DOMAIN_TARGET_LENGTH;
        var passwordCapacity = Native.CREDUI_MAX_PASSWORD_LENGTH;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var user = new StringBuilder(userCapacity);
            var domain = new StringBuilder(domainCapacity);
            var password = new StringBuilder(passwordCapacity);

            var ok = Native.CredUnPackAuthenticationBuffer(
                unpackFlags, buffer, size,
                user, ref userCapacity,
                domain, ref domainCapacity,
                password, ref passwordCapacity);
            var error = ok ? 0 : Marshal.GetLastWin32Error();

            if (ok)
            {
                report.AppendLine($"  attempt {attempt}: unpack SUCCEEDED (flags=0x{unpackFlags:X})");
                report.AppendLine($"    userName='{user}' (length={user.Length})");
                report.AppendLine($"    domain='{domain}' (length={domain.Length})");
                report.AppendLine($"    password {DescribeSecretShape(password.ToString())}");
                Overwrite(password);
                return report.ToString();
            }

            report.AppendLine($"  attempt {attempt}: unpack FAILED (flags=0x{unpackFlags:X}) lastError={error} " +
                              $"({new System.ComponentModel.Win32Exception(error).Message.Trim()})");
            report.AppendLine($"    capacities now user={userCapacity} domain={domainCapacity} password={passwordCapacity}");
            Overwrite(password);

            if (error != Native.ERROR_INSUFFICIENT_BUFFER)
            {
                return report.ToString();
            }
        }

        return report.ToString();
    }

    private static void Overwrite(StringBuilder builder)
    {
        for (var i = 0; i < builder.Length; i++)
        {
            builder[i] = '\0';
        }

        builder.Clear();
    }
}
