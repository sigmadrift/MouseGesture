using Avalonia;
using MouseGesture.App.Services;
using System;

namespace MouseGesture.App;

internal static class Program
{
    // Held for the entire process lifetime by the primary instance.
    private static Mutex? _instanceMutex;

    /// <summary>Signaled by a second instance to ask this one to show its settings window.</summary>
    public static EventWaitHandle? ShowSettingsEvent { get; private set; }

    /// <summary>Signaled by <c>--exit</c> to ask this instance to shut down gracefully.</summary>
    public static EventWaitHandle? ExitEvent { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Logger.Initialize();

        // Installer hooks: run the command and exit, without UI or single-instance checks.
        if (args.Length == 1)
        {
            switch (args[0])
            {
                case "--register-autostart":
                    return RunAutoStartCommand(enable: true);
                case "--unregister-autostart":
                    return RunAutoStartCommand(enable: false);
                case "--exit":
                    var exited = SingleInstance.RequestExit(TimeSpan.FromSeconds(10));
                    Logger.Info($"--exit: running instance {(exited ? "stopped" : "did not stop in time")}");
                    return exited ? 0 : 1;
            }
        }

        Logger.Info("=== MouseGesture starting ===");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error("UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        _instanceMutex = SingleInstance.TryAcquire(out var showSettingsEvent, out var exitEvent);
        if (_instanceMutex is null)
        {
            Logger.Info("Another instance is running — signaled it to surface settings, exiting.");
            return 0;
        }
        ShowSettingsEvent = showSettingsEvent;
        ExitEvent = exitEvent;

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error in Avalonia lifetime", ex);
            return 1;
        }
        finally
        {
            ShowSettingsEvent?.Dispose();
            ShowSettingsEvent = null;
            ExitEvent?.Dispose();
            ExitEvent = null;
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            _instanceMutex?.Dispose();
            _instanceMutex = null;
            Logger.Info("=== MouseGesture exited ===");
        }
    }

    private static int RunAutoStartCommand(bool enable)
    {
        var result = AutoStartManager.Set(enable);
        Logger.Info($"Autostart command ({(enable ? "register" : "unregister")}): {(result.Success ? "ok" : result.Message)}");
        return result.Success ? 0 : 1;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
