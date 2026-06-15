namespace MouseGesture.App.Services;

/// <summary>
/// Per-user single-instance guard built on a named Mutex plus a named auto-reset
/// EventWaitHandle that the second instance signals to ask the first to surface
/// its settings window.
/// </summary>
public static class SingleInstance
{
    // Local\ scope = per-session; avoids cross-user collisions on multi-user machines.
    private const string MutexName = @"Local\MouseGesture.SingleInstance";
    private const string EventName = @"Local\MouseGesture.ShowSettings";

    /// <summary>
    /// Tries to become the primary instance. On success, returns the held mutex (caller must keep it
    /// alive for the process lifetime). On failure, signals the existing instance and returns null.
    /// </summary>
    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
            return mutex;

        mutex.Dispose();
        TrySignalShowSettings();
        return null;
    }

    private static void TrySignalShowSettings()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var ev))
            {
                ev.Set();
                ev.Dispose();
            }
        }
        catch
        {
        }
    }

    /// <summary>Creates the named event the primary instance listens on.</summary>
    public static EventWaitHandle CreateShowSettingsEvent()
        => new(initialState: false, mode: EventResetMode.AutoReset, name: EventName);
}
