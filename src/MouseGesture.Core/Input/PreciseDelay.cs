using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseGesture.Core.Input;

/// <summary>
/// Sub-15 ms waits. Thread.Sleep(1) actually sleeps for the system timer period (~15.6 ms
/// by default), which made amplified scrolling crawl (x20 took ~300 ms per notch). Uses a
/// high-resolution waitable timer (Windows 10 1803+), falling back to a yielding spin.
/// Not thread-safe: use one instance per thread.
/// </summary>
internal sealed partial class PreciseDelay : IDisposable
{
    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_ALL_ACCESS = 0x001F0003;
    private const uint INFINITE = 0xFFFFFFFF;

    private IntPtr _timer = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);

    public void Wait(TimeSpan duration)
    {
        if (_timer != IntPtr.Zero)
        {
            var due = -duration.Ticks; // negative = relative, in 100 ns units
            if (SetWaitableTimer(_timer, in due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                WaitForSingleObject(_timer, INFINITE);
                return;
            }
        }

        var until = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < until)
            Thread.Yield();
    }

    public void Dispose()
    {
        if (_timer != IntPtr.Zero)
        {
            CloseHandle(_timer);
            _timer = IntPtr.Zero;
        }
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(IntPtr hTimer, in long lpDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);
}
