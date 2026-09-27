using System.Collections.Frozen;
using MouseGesture.Core.Native;

namespace MouseGesture.Core.Recognition;

/// <summary>
/// Answers "should we leave this press alone?" for the window under the cursor:
/// true for excluded processes and (optionally) fullscreen windows such as games.
/// Called on the hook thread at button DOWN only, so it must stay cheap.
/// </summary>
public sealed class ScreenProbe
{
    private volatile FrozenSet<string> _excluded = FrozenSet<string>.Empty;
    private volatile bool _disableInFullscreen;

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

    /// <summary>Lower-cased image file name of the process owning the window under the cursor, or null.</summary>
    public static string? GetProcessNameUnderCursor()
    {
        if (!Win32.GetCursorPos(out var pt))
            return null;
        var hwnd = Win32.WindowFromPoint(pt);
        return hwnd == IntPtr.Zero ? null : GetProcessName(Win32.GetAncestor(hwnd, Win32.GA_ROOT));
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
