namespace MouseGesture.App.Services;

/// <summary>
/// Minimal append-only logger writing to %LOCALAPPDATA%\MouseGesture\logs\app-yyyyMMdd.log.
/// The file rolls over at midnight (the app typically runs for weeks), and files older
/// than <see cref="RetentionDays"/> are pruned at startup and on each rollover.
/// </summary>
public static class Logger
{
    private const int RetentionDays = 14;

    private static readonly object _lock = new();
    private static string? _dir;
    private static DateOnly _currentDay;
    private static string? _path;

    public static string? LogDirectory => _dir;

    public static void Initialize()
    {
        try
        {
            _dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MouseGesture",
                "logs");
            Directory.CreateDirectory(_dir);
        }
        catch
        {
            // Logging must never crash the app.
            _dir = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        if (_dir is null)
            return;
        var now = DateTime.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        try
        {
            lock (_lock)
            {
                var today = DateOnly.FromDateTime(now);
                if (_path is null || today != _currentDay)
                {
                    _currentDay = today;
                    _path = Path.Combine(_dir, $"app-{now:yyyyMMdd}.log");
                    Prune();
                }
                File.AppendAllText(_path, line);
            }
        }
        catch
        {
        }
    }

    private static void Prune()
    {
        if (_dir is null)
            return;
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var f in Directory.EnumerateFiles(_dir, "app-*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
            }
        }
        catch
        {
        }
    }
}
