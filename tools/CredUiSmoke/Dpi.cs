namespace CredUiSmoke;

/// <summary>
///     Per-thread DPI awareness, for the two things that need honest screen pixels: measuring an
///     element so the mouse can be put on it, and blitting the screen. On a scaled display an
///     unaware thread is handed virtualized coordinates, which puts a click in the wrong place and
///     a capture off by the scale factor.
///     <para>
///         Per-thread rather than per-process on purpose. Some commands host the credential dialog
///         on an STA thread inside this same process, and declaring the whole process DPI-aware
///         would change how credui draws the very thing being photographed.
///     </para>
/// </summary>
internal readonly struct DpiScope : IDisposable
{
    private readonly IntPtr _previous;

    private DpiScope(IntPtr previous) => _previous = previous;

    internal static DpiScope Enter()
    {
        try
        {
            return new DpiScope(
                Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1607. Nothing to restore, and nothing to be done about the scaling either.
            return new DpiScope(IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        if (_previous != IntPtr.Zero)
        {
            Native.SetThreadDpiAwarenessContext(_previous);
        }
    }
}
