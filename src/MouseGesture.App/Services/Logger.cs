namespace MouseGesture.App.Services;

/// <summary>
/// Minimal append-only logger writing to %LOCALAPPDATA%\MouseGesture\logs\app-yyyyMMdd.log.
/// Files older than <see cref="RetentionDays"/> are pruned at startup.
/// </summary>
public static class Logger
{
    private const int RetentionDays = 14;

    private static readonly object _lock = new();
    private static string? _path;
    private static string? _dir;

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
            _path = Path.Combine(_dir, $"app-{DateTime.Now:yyyyMMdd}.log");
            Prune();
        }
        catch
        {
            // Logging must never crash the app.
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        var path = _path;
        if (path is null)
            return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        try
        {
            lock (_lock)
                File.AppendAllText(path, line);
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
