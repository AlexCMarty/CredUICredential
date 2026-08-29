using System.Drawing;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace CredUiSmoke;

/// <summary>
///     PNG captures of the real dialog. The UI Automation tree says which elements exist; it does
///     not say what the credential provider actually drew, and the peek glyph and the "More
///     choices" tiles are exactly the surface no test in <c>tests/</c> can see.
/// </summary>
internal static class Screenshot
{
    private static string? _directory;
    private static int _sequence;

    /// <summary>Nothing is captured unless a command was asked for it.</summary>
    internal static bool Enabled { get; set; }

    /// <summary>
    ///     Capture the whole virtual desktop rather than just the dialog. Off by default: the
    ///     dialog's own rectangle is the only part of the screen this harness has any business
    ///     writing to a file.
    /// </summary>
    internal static bool FullScreen { get; set; }

    internal static string? DirectoryOverride { get; set; }

    internal static string OutputDirectory
    {
        get
        {
            if (_directory is null)
            {
                _directory = DirectoryOverride ?? Path.Combine(
                    Path.GetTempPath(),
                    "CredUiSmoke",
                    $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
                System.IO.Directory.CreateDirectory(_directory);
            }

            return _directory;
        }
    }

    /// <summary>
    ///     Captures the dialog, or the whole desktop with <see cref="FullScreen" />. Returns the
    ///     file it wrote, or null if capture is off or could not be done - never throws, because a
    ///     screenshot failing is not a reason to strand a modal dialog on somebody's desktop.
    /// </summary>
    internal static string? Capture(AutomationElement? dialog, string tag)
    {
        if (!Enabled)
        {
            return null;
        }

        // Both the measurement and the blit have to run DPI-aware, or a scaled display hands back
        // virtualized coordinates and the capture is offset and blurry. Per-thread, so nothing
        // that draws the dialog is disturbed.
        var previousContext = SetDpiAware();
        try
        {
            var bounds = FullScreen || dialog is null ? VirtualScreen() : WindowBounds(dialog);
            if (bounds.Width < 1 || bounds.Height < 1)
            {
                Console.WriteLine($"screenshot ({tag}): nothing to capture, the window reported an empty rectangle.");
                return null;
            }

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            var path = Path.Combine(OutputDirectory, $"{++_sequence:00}-{Sanitize(tag)}.png");
            bitmap.Save(path, ImageFormat.Png);

            // A prompt on the secure desktop, or one the OS declines to let us read, comes back as
            // a single flat colour rather than as an error.
            var blank = IsUniform(bitmap)
                ? "  WARNING: uniform colour, so nothing was really captured (a CREDUIWIN_SECURE_PROMPT dialog cannot be)"
                : string.Empty;
            Console.WriteLine(
                $"screenshot ({tag}): {path}  {bounds.Width}x{bounds.Height} at {bounds.Left},{bounds.Top}{blank}");
            return path;
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or ExternalException
                                              or IOException or UnauthorizedAccessException
                                              or ArgumentException)
        {
            Console.WriteLine($"screenshot ({tag}) failed: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
        finally
        {
            if (previousContext != IntPtr.Zero)
            {
                Native.SetThreadDpiAwarenessContext(previousContext);
            }
        }
    }

    /// <summary>
    ///     The window rectangle, and not a pixel more: whatever else is on the desktop is none of
    ///     this harness's business. <c>GetWindowRect</c> is preferred over the automation element's
    ///     own rectangle because the dialog belongs to <c>CredentialUIBroker.exe</c> and its
    ///     bounding rectangle can lag a tile change.
    /// </summary>
    private static Rectangle WindowBounds(AutomationElement dialog, int margin = 0)
    {
        var handle = new IntPtr(dialog.Current.NativeWindowHandle);
        Rectangle bounds;
        if (handle != IntPtr.Zero && Native.GetWindowRect(handle, out var rect))
        {
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
        else
        {
            var box = dialog.Current.BoundingRectangle;
            bounds = box.IsEmpty
                ? Rectangle.Empty
                : Rectangle.FromLTRB((int)box.Left, (int)box.Top, (int)box.Right, (int)box.Bottom);
        }

        if (bounds.IsEmpty)
        {
            return bounds;
        }

        bounds.Inflate(margin, margin);
        return Rectangle.Intersect(bounds, VirtualScreen());
    }

    private static Rectangle VirtualScreen()
        => new(
            Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));

    private static IntPtr SetDpiAware()
    {
        try
        {
            return Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>A grid sample: enough to tell a real dialog from a flat rectangle of nothing.</summary>
    private static bool IsUniform(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0);
        for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 32))
        {
            for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 32))
            {
                if (bitmap.GetPixel(x, y) != first)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string Sanitize(string tag)
    {
        var clean = new string(tag.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return clean.Length == 0 ? "shot" : clean;
    }
}
