using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace MouseGesture.App.Services;

/// <summary>
/// Manages "run at logon". Because the app runs elevated (requireAdministrator),
/// a plain HKCU\...\Run entry would trigger a UAC prompt at every logon, so we use
/// a Scheduled Task with "run with highest privileges" instead, which starts the
/// app elevated and silently. Any legacy Run-key entry is removed on enable.
/// </summary>
public static class AutoStartManager
{
    private const string TaskName = "MouseGesture";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "MouseGesture";

    public static bool IsEnabled()
    {
        try
        {
            return RunSchTasks($"/Query /TN \"{TaskName}\"") == 0;
        }
        catch (Exception ex)
        {
            Logger.Warn($"AutoStart query failed: {ex.Message}");
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            if (enabled)
                Enable();
            else
                Disable();
        }
        catch (Exception ex)
        {
            Logger.Error("AutoStart write failed", ex);
        }
    }

    private static void Enable()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            Logger.Warn("AutoStart enable skipped: ProcessPath unavailable.");
            return;
        }

        var user = WindowsIdentity.GetCurrent().Name;
        // /RL HIGHEST → elevated without UAC prompt; /SC ONLOGON + /RU → this user's logon.
        var args =
            $"/Create /TN \"{TaskName}\" /TR \"\\\"{path}\\\"\" /SC ONLOGON /RL HIGHEST /RU \"{user}\" /F";
        var code = RunSchTasks(args);
        if (code == 0)
        {
            Logger.Info($"AutoStart enabled (scheduled task) → {path}");
            RemoveLegacyRunEntry();
        }
        else
        {
            Logger.Warn($"AutoStart enable failed: schtasks exit {code}");
        }
    }

    private static void Disable()
    {
        var code = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
        if (code == 0)
            Logger.Info("AutoStart disabled (scheduled task removed)");
        else
            Logger.Warn($"AutoStart disable: schtasks exit {code}");
        RemoveLegacyRunEntry();
    }

    private static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, writable: true);
            if (key?.GetValue(LegacyValueName) is not null)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                Logger.Info("Removed legacy Run-key autostart entry.");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Legacy Run-key cleanup failed: {ex.Message}");
        }
    }

    private static int RunSchTasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return -1;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
