using System.Runtime.InteropServices;
using System.Text;

namespace CredUiSmoke;

/// <summary>
///     One call to <c>CredUIPromptForWindowsCredentials</c>, on its own STA thread, with the
///     output buffer left alive long enough to be examined before it is freed.
/// </summary>
internal sealed class PromptRunner
{
    private readonly ManualResetEventSlim _finished = new(false);
    private Thread? _thread;

    internal int Flags { get; init; }
    internal uint InputAuthPackage { get; init; }
    internal string? SeedUserName { get; init; }
    internal int AuthError { get; init; }
    internal string Caption { get; init; } = "CredUiSmoke";
    internal string Message { get; init; } = "CredUiSmoke probe";

    internal int ReturnCode { get; private set; } = -1;
    internal uint OutputAuthPackage { get; private set; }
    internal IntPtr OutputBuffer { get; private set; }
    internal uint OutputBufferSize { get; private set; }
    internal bool SaveChecked { get; private set; }
    internal Exception? Failure { get; private set; }
    internal bool Finished => _finished.IsSet;

    internal void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "credui-prompt" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    internal bool Wait(TimeSpan timeout) => _finished.Wait(timeout);

    private void Run()
    {
        var inBuffer = IntPtr.Zero;
        uint inSize = 0;
        try
        {
            if (!string.IsNullOrEmpty(SeedUserName))
            {
                if (!TryPack(SeedUserName!, out inBuffer, out inSize, out var packError))
                {
                    Failure = new System.ComponentModel.Win32Exception(packError, "CredPackAuthenticationBuffer failed");
                    return;
                }
            }

            var info = new Native.CREDUI_INFO
            {
                cbSize = Marshal.SizeOf<Native.CREDUI_INFO>(),
                hwndParent = IntPtr.Zero,
                pszCaptionText = Caption,
                pszMessageText = Message,
                hbmBanner = IntPtr.Zero,
            };

            var package = InputAuthPackage;
            var save = false;
            ReturnCode = Native.CredUIPromptForWindowsCredentials(
                ref info,
                AuthError,
                ref package,
                inBuffer,
                inSize,
                out var outBuffer,
                out var outSize,
                ref save,
                Flags);

            OutputAuthPackage = package;
            OutputBuffer = outBuffer;
            OutputBufferSize = outSize;
            SaveChecked = save;
        }
        catch (Exception exception)
        {
            Failure = exception;
        }
        finally
        {
            if (inBuffer != IntPtr.Zero)
            {
                Native.CoTaskMemFree(inBuffer);
            }

            _finished.Set();
        }
    }

    /// <summary>
    ///     Releases the output buffer. It holds the password in the clear until it does, so this
    ///     runs whether or not anything could be made of it.
    /// </summary>
    internal void FreeOutputBuffer()
    {
        if (OutputBuffer == IntPtr.Zero)
        {
            return;
        }

        if (OutputBufferSize > 0)
        {
            var zeros = new byte[OutputBufferSize];
            Marshal.Copy(zeros, 0, OutputBuffer, (int)OutputBufferSize);
        }

        Native.CoTaskMemFree(OutputBuffer);
        OutputBuffer = IntPtr.Zero;
    }

    private static bool TryPack(string userName, out IntPtr buffer, out uint size, out int error)
    {
        buffer = IntPtr.Zero;
        size = 0;
        error = 0;

        var bytes = 0;
        Native.CredPackAuthenticationBuffer(0, userName, string.Empty, IntPtr.Zero, ref bytes);
        if (bytes <= 0)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        buffer = Marshal.AllocCoTaskMem(bytes);
        if (!Native.CredPackAuthenticationBuffer(0, userName, string.Empty, buffer, ref bytes))
        {
            error = Marshal.GetLastWin32Error();
            Marshal.FreeCoTaskMem(buffer);
            buffer = IntPtr.Zero;
            return false;
        }

        size = (uint)bytes;
        return true;
    }

    internal string DescribeOutcome(Dictionary<uint, List<string>> packageMap)
    {
        var report = new StringBuilder();
        if (Failure is not null)
        {
            report.AppendLine($"prompt threw: {Failure.GetType().Name}: {Failure.Message}");
            return report.ToString();
        }

        var rcName = ReturnCode switch
        {
            Native.NO_ERROR => "NO_ERROR (OK)",
            Native.ERROR_CANCELLED => "ERROR_CANCELLED",
            -1 => "<never returned>",
            _ => new System.ComponentModel.Win32Exception(ReturnCode).Message.Trim(),
        };
        report.AppendLine($"returnCode={ReturnCode} ({rcName})");

        if (ReturnCode != Native.NO_ERROR)
        {
            return report.ToString();
        }

        var names = packageMap.TryGetValue(OutputAuthPackage, out var found)
            ? string.Join("/", found)
            : "<no LSA name matched>";
        report.AppendLine($"outAuthPackage={OutputAuthPackage} ({names})");
        report.AppendLine($"inputAuthPackage was {InputAuthPackage} -> " +
                          (OutputAuthPackage == InputAuthPackage
                              ? "UNCHANGED (credui may simply have left the input value in place)"
                              : "written by credui"));
        report.AppendLine($"saveChecked={SaveChecked}");
        report.AppendLine($"outBufferSize={OutputBufferSize} bytes");

        var leading = Diagnostics.ReadLeadingDword(OutputBuffer, OutputBufferSize);
        report.AppendLine(leading is null
            ? "leadingDword=<buffer too small to read>"
            : $"leadingDword={leading} ({Diagnostics.DescribeMessageType(leading.Value)})");

        report.AppendLine("unpack with flags=0:");
        report.Append(Diagnostics.TryUnpack(OutputBuffer, OutputBufferSize, 0));
        report.AppendLine("unpack with CRED_PACK_PROTECTED_CREDENTIALS:");
        report.Append(Diagnostics.TryUnpack(OutputBuffer, OutputBufferSize, Native.CRED_PACK_PROTECTED_CREDENTIALS));

        // What the module itself would decide. It reads the buffer's KERB_LOGON_SUBMIT_TYPE, not
        // outAuthPackage: credui echoes back whatever pulAuthPackage was seeded with, so that
        // value describes the request rather than the credential.
        report.AppendLine($"module verdict: emits a PSCredential = " +
                          (leading == Native.KerbInteractiveLogon).ToString().ToLowerInvariant());
        return report.ToString();
    }
}
