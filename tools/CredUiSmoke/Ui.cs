using System.Text;
using System.Windows.Automation;

namespace CredUiSmoke;

/// <summary>
///     Driving and observing the credential dialog from the outside. The dialog is created on a
///     dedicated STA thread inside this same process, so every element it owns carries this
///     process id - a far steadier hook than matching on the "Windows Security" caption.
/// </summary>
internal static class Ui
{
    internal static AutomationElement? WaitForDialog(int processId, string messageText, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(150);
            try
            {
                // The message text carries the harness's own process id, so it identifies our
                // dialog on its own. Matching on the "Windows Security" caption alone would not:
                // a prompt stranded by an earlier probe looks exactly the same, and driving
                // somebody else's dialog is worse than failing to find our own.
                foreach (AutomationElement candidate in AutomationElement.RootElement.FindAll(
                             TreeScope.Children, Condition.TrueCondition))
                {
                    if (Owns(candidate, processId) && IsCredentialDialog(candidate, messageText))
                    {
                        return candidate;
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        return null;
    }

    /// <summary>
    ///     Finds a credential dialog raised by another process - the PowerShell session running
    ///     the real cmdlet - by the message text that process asked for.
    /// </summary>
    internal static AutomationElement? WaitForForeignDialog(string messageText, TimeSpan timeout)
        => WaitForDialog(0, messageText, timeout);

    /// <summary>
    ///     On Windows 11 the credential dialog is drawn by <c>CredentialUIBroker.exe</c>, not by
    ///     the process that called <c>CredUIPromptForWindowsCredentials</c>, so the window carries
    ///     the broker's process id. Matching on the caller's id finds nothing; the message text is
    ///     the only thing the caller controls that reaches the window.
    /// </summary>
    private static bool Owns(AutomationElement candidate, int processId)
    {
        try
        {
            return processId == 0
                   || candidate.Current.ProcessId == processId
                   || (candidate.Current.Name ?? string.Empty).Contains("Windows Security", StringComparison.OrdinalIgnoreCase);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool IsCredentialDialog(AutomationElement candidate, string messageText)
    {
        try
        {
            return candidate.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, messageText)) is not null;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    internal static string DumpTree(AutomationElement root, bool includeRects)
    {
        var report = new StringBuilder();
        Walk(root, 0, report, includeRects);
        return report.ToString();
    }

    private static void Walk(AutomationElement element, int depth, StringBuilder report, bool includeRects)
    {
        if (depth > 12)
        {
            return;
        }

        try
        {
            var current = element.Current;
            var indent = new string(' ', depth * 2);
            var rect = includeRects && !current.BoundingRectangle.IsEmpty
                ? $" rect=({current.BoundingRectangle.Left:0},{current.BoundingRectangle.Top:0},{current.BoundingRectangle.Width:0}x{current.BoundingRectangle.Height:0})"
                : string.Empty;
            var password = current.IsPassword ? " IsPassword=true" : string.Empty;
            var offscreen = current.IsOffscreen ? " offscreen" : string.Empty;
            report.AppendLine(
                $"{indent}{Short(current.ControlType.ProgrammaticName)} name='{current.Name}' id='{current.AutomationId}' " +
                $"class='{current.ClassName}'{password}{offscreen}{rect}");

            foreach (AutomationElement child in element.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                Walk(child, depth + 1, report, includeRects);
            }
        }
        catch (ElementNotAvailableException)
        {
            report.AppendLine(new string(' ', depth * 2) + "<element went away>");
        }
    }

    private static string Short(string programmaticName)
        => programmaticName.StartsWith("ControlType.", StringComparison.Ordinal)
            ? programmaticName["ControlType.".Length..]
            : programmaticName;

    internal static IEnumerable<AutomationElement> Descendants(AutomationElement root)
    {
        AutomationElementCollection all;
        try
        {
            all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        }
        catch (ElementNotAvailableException)
        {
            yield break;
        }

        foreach (AutomationElement element in all)
        {
            yield return element;
        }
    }

    internal static AutomationElement? FindByNameContains(AutomationElement root, params string[] fragments)
    {
        foreach (var element in Descendants(root))
        {
            string name;
            try
            {
                name = element.Current.Name ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            foreach (var fragment in fragments)
            {
                if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    return element;
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     The first editable, non-password field - the user name box on the password tile.
    /// </summary>
    internal static AutomationElement? FindUserNameField(AutomationElement root)
        => Descendants(root).FirstOrDefault(e => IsEdit(e) && !IsPassword(e));

    internal static AutomationElement? FindPasswordField(AutomationElement root)
        => Descendants(root).FirstOrDefault(IsPassword);

    private static bool IsEdit(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType == ControlType.Edit
                   || element.Current.ControlType == ControlType.ComboBox;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool IsPassword(AutomationElement element)
    {
        try
        {
            return element.Current.IsPassword;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    internal static bool Invoke(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
                return true;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
            {
                ((SelectionItemPattern)select).Select();
                return true;
            }

            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand))
            {
                ((ExpandCollapsePattern)expand).Expand();
                return true;
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
        }

        return false;
    }

    /// <summary>
    ///     Types a secret into whatever has focus. Uses <c>SendInput</c> with
    ///     <c>KEYEVENTF_UNICODE</c> rather than <c>SendKeys</c>: it needs no escaping, so a
    ///     password containing <c>{</c> or <c>%</c> arrives as typed.
    /// </summary>
    internal static void TypeText(string text, int perCharacterDelayMs = 12)
    {
        foreach (var character in text)
        {
            SendUnicode(character, up: false);
            SendUnicode(character, up: true);
            Thread.Sleep(perCharacterDelayMs);
        }
    }

    private static void SendUnicode(char character, bool up)
    {
        var input = new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.INPUTUNION
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = character,
                    dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
        Native.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
    }

    internal static void SendVirtualKey(ushort key)
    {
        var down = new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.INPUTUNION { ki = new Native.KEYBDINPUT { wVk = key } },
        };
        var up = down;
        up.u.ki.dwFlags = Native.KEYEVENTF_KEYUP;
        Native.SendInput(2, new[] { down, up }, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
    }

    internal static bool Focus(AutomationElement element)
    {
        try
        {
            element.SetFocus();
            Thread.Sleep(120);
            return true;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    ///     The escape hatch. Anything that opens the dialog must be able to close it without a
    ///     human, or an unattended run blocks forever on a modal window nobody can see.
    /// </summary>
    internal static string Cancel(AutomationElement? dialog)
    {
        if (dialog is not null)
        {
            var cancel = dialog.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, "Cancel")));
            if (cancel is not null && Invoke(cancel))
            {
                return "cancelled via Cancel button";
            }

            try
            {
                var handle = new IntPtr(dialog.Current.NativeWindowHandle);
                if (handle != IntPtr.Zero)
                {
                    Native.SetForegroundWindow(handle);
                    Thread.Sleep(80);
                    SendVirtualKey(Native.VK_ESCAPE);
                    Thread.Sleep(250);
                    Native.PostMessage(handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    return "cancelled via ESC + WM_CLOSE";
                }
            }
            catch (ElementNotAvailableException)
            {
                return "dialog vanished before cancel";
            }
        }

        SendVirtualKey(Native.VK_ESCAPE);
        return "cancelled via bare ESC (no dialog element)";
    }
}
