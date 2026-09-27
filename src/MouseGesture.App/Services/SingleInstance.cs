namespace MouseGesture.App.Services;

/// <summary>
/// Per-session single-instance guard built on a named Mutex plus named auto-reset events
/// that other launches signal: "show settings" (a second normal launch) and "exit"
/// (<c>--exit</c>, used by the installer for a graceful shutdown).
/// </summary>
public static class SingleInstance
{
    // Local\ scope = per-session; avoids cross-user collisions on multi-user machines.
    private const string MutexName = @"Local\MouseGesture.SingleInstance";
    private const string ShowSettingsEventName = @"Local\MouseGesture.ShowSettings";
    private const string ExitEventName = @"Local\MouseGesture.Exit";

    /// <summary>
    /// Tries to become the primary instance. On success, returns the held mutex (caller must keep it
    /// alive for the process lifetime) and the signal events, created right away so a launch during
    /// startup can't miss them (an auto-reset event stays signaled until the listener starts waiting).
    /// On failure, signals the existing instance to show its settings and returns null.
    /// </summary>
    public static Mutex? TryAcquire(out EventWaitHandle? showSettingsEvent, out EventWaitHandle? exitEvent)
    {
        showSettingsEvent = null;
        exitEvent = null;
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            TrySignal(ShowSettingsEventName);
            return null;
        }

        showSettingsEvent = TryCreateEvent(ShowSettingsEventName);
        exitEvent = TryCreateEvent(ExitEventName);
        return mutex;
    }

    /// <summary>
    /// Asks the running instance to shut down and waits for it to release the instance mutex.
    /// Returns true when no instance is running afterwards.
    /// </summary>
    public static bool RequestExit(TimeSpan timeout)
    {
        if (!Mutex.TryOpenExisting(MutexName, out var mutex))
            return true;
        using (mutex)
        {
            TrySignal(ExitEventName);
            try
            {
                if (!mutex.WaitOne(timeout))
                    return false;
            }
            catch (AbandonedMutexException)
            {
                // The primary died without releasing it; we now own it — that's "not running".
            }
            mutex.ReleaseMutex();
            return true;
        }
    }

    private static EventWaitHandle? TryCreateEvent(string name)
    {
        try
        {
            return new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Creating event {name} failed: {ex.Message}");
            return null;
        }
    }

    private static void TrySignal(string eventName)
    {
        // The primary may still be starting up; give it a moment to create the event.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(eventName, out var ev))
                {
                    ev.Set();
                    ev.Dispose();
                    return;
                }
            }
            catch
            {
                return;
            }
            Thread.Sleep(100);
        }
    }
}
