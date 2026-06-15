using Avalonia;
using MouseGesture.App.Services;
using System;

namespace MouseGesture.App;

internal static class Program
{
    // Held for the entire process lifetime by the primary instance.
    private static Mutex? _instanceMutex;

    [STAThread]
    public static int Main(string[] args)
    {
        Logger.Initialize();
        Logger.Info("=== MouseGesture starting ===");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error("UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        _instanceMutex = SingleInstance.TryAcquire();
        if (_instanceMutex is null)
        {
            Logger.Info("Another instance is running — signaled it to surface settings, exiting.");
            return 0;
        }

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
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            _instanceMutex?.Dispose();
            _instanceMutex = null;
            Logger.Info("=== MouseGesture exited ===");
        }
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
