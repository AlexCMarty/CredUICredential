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
    internal static AutomationElement? WaitForDialog(
        int processId, string messageText, TimeSpan timeout, IReadOnlySet<int>? ignoreHandles = null)
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
                    if (ignoreHandles is not null && WasAlreadyThere(candidate, ignoreHandles))
                    {
                        continue;
                    }

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
    ///     the real cmdlet - by the message text that process asked for. Pass the handles from
    ///     <see cref="CredentialWindowHandles" />, taken before that process was started, to be
    ///     sure of getting the new dialog rather than one stranded by an earlier run: when the
    ///     message is the cmdlet's own default the text alone does not tell them apart.
    /// </summary>
    internal static AutomationElement? WaitForForeignDialog(
        string messageText, TimeSpan timeout, IReadOnlySet<int>? ignoreHandles = null)
        => WaitForDialog(0, messageText, timeout, ignoreHandles);

    /// <summary>
    ///     Every credential dialog on the desktop right now, by window handle.
    /// </summary>
    internal static IReadOnlySet<int> CredentialWindowHandles()
    {
        var handles = new HashSet<int>();
        try
        {
            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                         TreeScope.Children,
                         new PropertyCondition(AutomationElement.NameProperty, "Windows Security")))
            {
                handles.Add(window.Current.NativeWindowHandle);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        return handles;
    }

    private static bool WasAlreadyThere(AutomationElement candidate, IReadOnlySet<int> handles)
    {
        try
        {
            return handles.Contains(candidate.Current.NativeWindowHandle);
        }
        catch (ElementNotAvailableException)
        {
            return true;
        }
    }

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

    // ---- the pointer ---------------------------------------------------------------------

    /// <summary>
    ///     The centre of an element, in real screen pixels. Measured inside a <see cref="DpiScope" />
    ///     because UI Automation hands a DPI-unaware thread virtualized coordinates, and a click
    ///     placed with those lands somewhere else entirely on a scaled display.
    /// </summary>
    internal static (int X, int Y)? CentreOf(AutomationElement element)
    {
        using var dpi = DpiScope.Enter();
        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1)
            {
                return null;
            }

            return ((int)(rect.Left + (rect.Width / 2)), (int)(rect.Top + (rect.Height / 2)));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Moves the pointer in absolute virtual-desktop coordinates. <c>SendInput</c> wants those
    ///     normalised to 0..65535 across the whole virtual desktop, not in pixels.
    /// </summary>
    internal static void MouseMove(int x, int y)
    {
        using var dpi = DpiScope.Enter();
        var left = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        var width = Math.Max(1, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN) - 1);
        var height = Math.Max(1, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN) - 1);
        SendMouse(
            Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK,
            (int)Math.Round((x - left) * 65535.0 / width),
            (int)Math.Round((y - top) * 65535.0 / height));
        Thread.Sleep(40);
    }

    private static void SendMouse(uint flags, int normalisedX = 0, int normalisedY = 0)
    {
        var input = new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.INPUTUNION
            {
                mi = new Native.MOUSEINPUT { dx = normalisedX, dy = normalisedY, dwFlags = flags },
            },
        };
        Native.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
    }

    /// <summary>
    ///     A real pointer click, for the affordances UI Automation will not invoke. Puts the cursor
    ///     back where it found it: leaving somebody else's pointer parked on a credential dialog is
    ///     rude, and confusing if a human is watching.
    /// </summary>
    internal static bool MouseClick(AutomationElement element)
    {
        var centre = CentreOf(element);
        if (centre is null)
        {
            return false;
        }

        var restore = SaveCursor();
        MouseMove(centre.Value.X, centre.Value.Y);
        SendMouse(Native.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(60);
        SendMouse(Native.MOUSEEVENTF_LEFTUP);
        Thread.Sleep(120);
        restore();
        return true;
    }

    /// <summary>
    ///     Presses the left button on an element, runs <paramref name="whileHeld" />, and only then
    ///     lets go. This is the whole point for the peek glyph: it is a hold-to-reveal affordance,
    ///     so the password is legible for exactly as long as the button is down. A click would
    ///     reveal and re-hide before anything could look.
    /// </summary>
    internal static bool PressAndHold(AutomationElement element, Action whileHeld)
    {
        var centre = CentreOf(element);
        if (centre is null)
        {
            return false;
        }

        var restore = SaveCursor();
        MouseMove(centre.Value.X, centre.Value.Y);
        SendMouse(Native.MOUSEEVENTF_LEFTDOWN);
        try
        {
            Thread.Sleep(250);
            whileHeld();
        }
        finally
        {
            // Never leave the button down. A stuck left button makes the desktop unusable for
            // whoever owns it, which is a far worse outcome than a missed screenshot.
            SendMouse(Native.MOUSEEVENTF_LEFTUP);
            Thread.Sleep(80);
            restore();
        }

        return true;
    }

    private static Action SaveCursor()
    {
        using var dpi = DpiScope.Enter();
        if (!Native.GetCursorPos(out var point))
        {
            return static () => { };
        }

        var x = point.X;
        var y = point.Y;
        return () => MouseMove(x, y);
    }

    // ---- the keyboard --------------------------------------------------------------------

    /// <summary>A modifier held down across a key press, e.g. Ctrl+A.</summary>
    internal static void SendChord(ushort modifier, ushort key)
    {
        var inputs = new[]
        {
            KeyInput(modifier, up: false),
            KeyInput(key, up: false),
            KeyInput(key, up: true),
            KeyInput(modifier, up: true),
        };
        Native.SendInput(
            (uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
        Thread.Sleep(60);
    }

    private static Native.INPUT KeyInput(ushort key, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT { wVk = key, dwFlags = up ? Native.KEYEVENTF_KEYUP : 0 },
        },
    };

    /// <summary>
    ///     Empties whatever field has focus. Needed to type over a user name the cmdlet seeded with
    ///     <c>-UserName</c>: that field arrives populated, and typing into it would otherwise
    ///     append to what is already there.
    /// </summary>
    internal static void ClearFocusedField()
    {
        SendChord(Native.VK_CONTROL, Native.VK_A);
        SendVirtualKey(Native.VK_DELETE);
        Thread.Sleep(80);
    }

    // ---- the peek glyph ------------------------------------------------------------------

    /// <summary>
    ///     Buttons sitting inside the password box's own rectangle. There is no stable
    ///     AutomationId for the peek glyph across Windows versions, so geometry is what identifies
    ///     it; the box has no other buttons in it.
    /// </summary>
    internal static AutomationElement[] PeekCandidates(AutomationElement dialog)
    {
        var passwordField = FindPasswordField(dialog);
        if (passwordField is null)
        {
            return Array.Empty<AutomationElement>();
        }

        try
        {
            var box = passwordField.Current.BoundingRectangle;
            if (box.IsEmpty)
            {
                return Array.Empty<AutomationElement>();
            }

            return Descendants(dialog)
                .Where(e => e.Current.ControlType == ControlType.Button)
                .Where(e =>
                {
                    var rect = e.Current.BoundingRectangle;
                    return !rect.IsEmpty
                           && rect.Top >= box.Top - 4 && rect.Bottom <= box.Bottom + 4
                           && rect.Left >= box.Left && rect.Right <= box.Right + 4;
                })
                .ToArray();
        }
        catch (ElementNotAvailableException)
        {
            return Array.Empty<AutomationElement>();
        }
    }

    /// <summary>
    ///     The peek glyph, by name and AutomationId where Windows offers them and by geometry where
    ///     it does not. It is only templated once the password box has something to reveal, so an
    ///     empty box legitimately has none - a fact about the moment, not a regression.
    /// </summary>
    internal static AutomationElement? FindRevealButton(AutomationElement dialog)
    {
        var candidates = PeekCandidates(dialog);
        foreach (var candidate in candidates)
        {
            try
            {
                var id = candidate.Current.AutomationId ?? string.Empty;
                var name = candidate.Current.Name ?? string.Empty;
                if (id.Contains("Reveal", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Show Password", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Reveal", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Peek", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        // Unnamed, but inside the password box and the only button there.
        return candidates.FirstOrDefault();
    }
}
