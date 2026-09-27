using System.Collections.Frozen;
using MouseGesture.Core.Native;

namespace MouseGesture.Core.Recognition;

/// <summary>An executable with a visible window, for picking apps to exclude.</summary>
public sealed record RunningApp(string ProcessName, string WindowTitle);

/// <summary>
/// Answers "should we leave this press alone?" for the window under the cursor:
/// true for excluded processes and (optionally) fullscreen windows such as games.
/// Called on the hook thread at button DOWN only, so it must stay cheap.
/// </summary>
public sealed class ScreenProbe
{
    private volatile FrozenSet<string> _excluded = FrozenSet<string>.Empty;
    private volatile bool _disableInFullscreen;

    // Shell/system processes that own titled top-level windows but aren't apps a user
    // would exclude (input host, Start menu, UWP frame host shared by all store apps, …).
    private static readonly FrozenSet<string> ShellProcesses = new[]
    {
        "textinputhost.exe", "applicationframehost.exe", "shellexperiencehost.exe",
        "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe", "lockapp.exe",
        "systemsettings.exe", "shellhost.exe",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public void Configure(IEnumerable<string> excludedProcessNames, bool disableInFullscreen)
    {
        _excluded = excludedProcessNames
            .Select(NormalizeProcessName)
            .Where(n => n.Length > 0)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _disableInFullscreen = disableInFullscreen;
    }

    /// <summary>"C:\x\Game.EXE " → "game.exe"; "game" → "game.exe".</summary>
    public static string NormalizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var file = Path.GetFileName(name.Trim()).ToLowerInvariant();
        if (file.Length == 0)
            return string.Empty;
        return Path.HasExtension(file) ? file : file + ".exe";
    }

    public bool ShouldBypass(int x, int y)
    {
        var excluded = _excluded;
        var checkFullscreen = _disableInFullscreen;
        if (excluded.Count == 0 && !checkFullscreen)
            return false;

        try
        {
            var hwnd = Win32.WindowFromPoint(new Win32.POINT { X = x, Y = y });
            if (hwnd == IntPtr.Zero)
                return false;
            var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
            if (root == IntPtr.Zero)
                root = hwnd;

            if (checkFullscreen && IsFullscreen(root))
                return true;
            if (excluded.Count > 0)
            {
                var name = GetProcessName(root);
                return name is not null && excluded.Contains(name);
            }
        }
        catch
        {
            // Never let a probe failure break input handling.
        }
        return false;
    }

    /// <summary>DPI scale (1.0 = 96 DPI) of the monitor containing the point.</summary>
    public static double GetDpiScale(int x, int y)
    {
        try
        {
            var monitor = Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, Win32.MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero && Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0)
                return dpi / 96.0;
        }
        catch
        {
        }
        return 1.0;
    }

    /// <summary>
    /// Apps that currently show a regular top-level window (roughly what the taskbar
    /// shows), one entry per executable, sorted by name. Excludes this process.
    /// </summary>
    public static unsafe IReadOnlyList<RunningApp> GetRunningApps()
    {
        var ownPid = (uint)Environment.ProcessId;
        var ownName = Path.GetFileName(Environment.ProcessPath ?? "").ToLowerInvariant();
        var byName = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        var title = stackalloc char[256];

        // Walk the top-level windows in Z order (front to back), so the first title seen
        // for an executable is its most recently used window.
        for (var hwnd = Win32.GetTopWindow(IntPtr.Zero); hwnd != IntPtr.Zero; hwnd = Win32.GetWindow(hwnd, Win32.GW_HWNDNEXT))
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero)
                continue;
            if (((long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0)
                continue;
            var len = Win32.GetWindowText(hwnd, title, 256);
            if (len <= 0)
                continue;

            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == ownPid)
                continue;
            var name = GetProcessName(hwnd);
            if (name is null || byName.ContainsKey(name) || name == ownName || ShellProcesses.Contains(name))
                continue;
            // Windows on other virtual desktops are cloaked by DWM, so cloaking alone can't
            // filter background windows; the shell-process list above handles the usual ones.
            byName[name] = new RunningApp(name, new string(title, 0, len));
        }

        return [.. byName.Values.OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)];
    }

    private static unsafe bool IsFullscreen(IntPtr hwnd)
    {
        if (hwnd == Win32.GetShellWindow() || hwnd == Win32.GetDesktopWindow())
            return false;

        // The desktop icon layer (and its WorkerW siblings) cover the whole monitor.
        var buffer = stackalloc char[32];
        var len = Win32.GetClassName(hwnd, buffer, 32);
        var cls = new string(buffer, 0, Math.Max(len, 0));
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false;

        // A maximized normal window keeps its caption; with an auto-hidden taskbar it can
        // cover the monitor too, so require the caption to be gone.
        var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        if ((style & Win32.WS_CAPTION) == Win32.WS_CAPTION)
            return false;

        if (!Win32.GetWindowRect(hwnd, out var rect))
            return false;
        var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = (uint)sizeof(Win32.MONITORINFO) };
        if (monitor == IntPtr.Zero || !Win32.GetMonitorInfo(monitor, ref info))
            return false;

        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    private static unsafe string? GetProcessName(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;
        var process = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            if (!Win32.QueryFullProcessImageName(process, 0, buffer, ref size))
                return null;
            return Path.GetFileName(new string(buffer, 0, (int)size)).ToLowerInvariant();
        }
        finally
        {
            Win32.CloseHandle(process);
        }
    }
}
