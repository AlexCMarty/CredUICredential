using System.Runtime.InteropServices;
using System.Text;

namespace CredUiSmoke;

/// <summary>
///     The credui / secur32 / user32 surface the harness needs. Calling convention is Winapi
///     everywhere, matching the module (Cdecl corrupts the stack on the x86 build of PowerShell).
/// </summary>
internal static class Native
{
    internal const int CREDUIWIN_GENERIC = 0x00000001;
    internal const int CREDUIWIN_CHECKBOX = 0x00000002;
    internal const int CREDUIWIN_AUTHPACKAGE_ONLY = 0x00000010;
    internal const int CREDUIWIN_IN_CRED_ONLY = 0x00000020;
    internal const int CREDUIWIN_ENUMERATE_ADMINS = 0x00000100;
    internal const int CREDUIWIN_ENUMERATE_CURRENT_USER = 0x00000200;
    internal const int CREDUIWIN_SECURE_PROMPT = 0x00001000;
    internal const int CREDUIWIN_PREPROMPTING = 0x00002000;
    internal const int CREDUIWIN_PACK_32_WOW = 0x10000000;

    internal const int CRED_PACK_PROTECTED_CREDENTIALS = 0x1;
    internal const int CRED_PACK_WOW_BUFFER = 0x2;
    internal const int CRED_PACK_GENERIC_CREDENTIALS = 0x4;
    internal const int CRED_PACK_ID_PROVIDER_CREDENTIALS = 0x8;

    /// <summary>KERB_LOGON_SUBMIT_TYPE.KerbInteractiveLogon - a user name and password.</summary>
    internal const uint KerbInteractiveLogon = 2;

    internal const int NO_ERROR = 0;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_CANCELLED = 1223;

    // Windows' published ceilings from wincred.h.
    internal const int CREDUI_MAX_USERNAME_LENGTH = 513;
    internal const int CREDUI_MAX_DOMAIN_TARGET_LENGTH = 337;
    internal const int CREDUI_MAX_PASSWORD_LENGTH = 256;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct CREDUI_INFO
    {
        public int cbSize;
        public IntPtr hwndParent;
        public string pszMessageText;
        public string pszCaptionText;
        public IntPtr hbmBanner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode, EntryPoint = "CredUIPromptForWindowsCredentialsW",
        CallingConvention = CallingConvention.Winapi)]
    internal static extern int CredUIPromptForWindowsCredentials(
        ref CREDUI_INFO pUiInfo,
        int dwAuthError,
        ref uint pulAuthPackage,
        IntPtr pvInAuthBuffer,
        uint ulInAuthBufferSize,
        out IntPtr ppvOutAuthBuffer,
        out uint pulOutAuthBufferSize,
        ref bool pfSave,
        int dwFlags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, EntryPoint = "CredPackAuthenticationBufferW",
        SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern bool CredPackAuthenticationBuffer(
        int dwFlags,
        string pszUserName,
        string pszPassword,
        IntPtr pPackedCredentials,
        ref int pcbPackedCredentials);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, EntryPoint = "CredUnPackAuthenticationBufferW",
        SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern bool CredUnPackAuthenticationBuffer(
        int dwFlags,
        IntPtr pAuthBuffer,
        uint cbAuthBuffer,
        StringBuilder? pszUserName,
        ref int pcchMaxUserName,
        StringBuilder? pszDomainName,
        ref int pcchMaxDomainName,
        StringBuilder? pszPassword,
        ref int pcchMaxPassword);

    [DllImport("ole32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern void CoTaskMemFree(IntPtr pv);

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
        IntPtr LsaHandle, ref LSA_STRING PackageName, out uint AuthenticationPackage);

    [DllImport("secur32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern int LsaDeregisterLogonProcess(IntPtr LsaHandle);

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern int GetSystemMetrics(int nIndex);

    internal const int SM_XVIRTUALSCREEN = 76;
    internal const int SM_YVIRTUALSCREEN = 77;
    internal const int SM_CXVIRTUALSCREEN = 78;
    internal const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>
    ///     Per-thread DPI awareness, so a screen capture on a scaled display measures and blits in
    ///     real pixels. Per-monitor v2 is -4, passed as a pseudo-handle.
    /// </summary>
    internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    internal const uint WM_CLOSE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_UNICODE = 0x0004;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_ESCAPE = 0x1B;
    internal const ushort VK_TAB = 0x09;

    [DllImport("user32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
