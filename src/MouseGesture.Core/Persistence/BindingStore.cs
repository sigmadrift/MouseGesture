using System.Text.Json;
using MouseGesture.Core.Actions;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Persistence;

/// <param name="CorruptBackupPath">
/// Set when the file existed but could not be parsed: the original was copied here
/// before defaults were used, so the next save can't silently destroy it.
/// </param>
/// <param name="SkippedEntries">Bindings dropped because of an invalid stroke or unknown action.</param>
public sealed record LoadResult(GestureMap Map, AppSettings Settings, string? CorruptBackupPath, int SkippedEntries);

/// <summary>Loads and saves gestures + settings as JSON in %APPDATA%/MouseGesture.</summary>
public sealed class BindingStore
{
    private readonly string _path;
    private readonly ActionRegistry _registry;

    public BindingStore(ActionRegistry registry, string? overridePath = null)
    {
        _registry = registry;
        _path = overridePath ?? DefaultPath();
    }

    public string FilePath => _path;

    public static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MouseGesture");
        return Path.Combine(dir, "bindings.json");
    }

    public LoadResult Load()
    {
        if (!File.Exists(_path))
            return new LoadResult(GestureMap.CreateDefault(), AppSettings.Default, null, 0);

        BindingsConfig? config;
        try
        {
            using var stream = File.OpenRead(_path);
            config = JsonSerializer.Deserialize(stream, BindingsJsonContext.Default.BindingsConfig);
        }
        catch
        {
            config = null;
        }
        if (config is null)
            return new LoadResult(GestureMap.CreateDefault(), AppSettings.Default, BackupUnreadableFile(), 0);

        var map = new GestureMap();
        var skipped = 0;
        foreach (var entry in config.Bindings ?? [])
        {
            var action = _registry.Get(entry.ActionId ?? "");
            if (!StrokeFormat.TryNormalize(entry.Stroke, out var stroke) || action is null)
            {
                skipped++;
                continue;
            }
            map.Bind(stroke, action);
        }

        var settings = new AppSettings(
            ParseButton(config.Trigger, TriggerButton.Right),
            new WheelSettings(
                config.WheelAmplifyEnabled,
                ParseButton(config.WheelModifierButton, WheelSettings.Default.Modifier),
                config.WheelMultiplier),
            config.HoldTimeoutMs,
            config.DisableInFullscreen,
            [.. config.ExcludedApps ?? []]).Normalized();

        return new LoadResult(map, settings, null, skipped);
    }

    /// <summary>Saves the map and settings atomically (write temp file, then replace).</summary>
    public void Save(GestureMap map, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var normalized = settings.Normalized();
        var config = new BindingsConfig
        {
            Trigger = normalized.Trigger.ToString(),
            Bindings = map.Bindings
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new BindingEntry { Stroke = p.Key, ActionId = p.Value.Id })
                .ToList(),
            WheelAmplifyEnabled = normalized.Wheel.Enabled,
            WheelModifierButton = normalized.Wheel.Modifier.ToString(),
            WheelMultiplier = normalized.Wheel.Multiplier,
            HoldTimeoutMs = normalized.HoldTimeoutMs,
            DisableInFullscreen = normalized.DisableInFullscreen,
            ExcludedApps = [.. normalized.ExcludedApps],
        };

        var tmp = _path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, config, BindingsJsonContext.Default.BindingsConfig);
        File.Move(tmp, _path, overwrite: true);
    }

    private string? BackupUnreadableFile()
    {
        try
        {
            var backup = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_path, backup, overwrite: true);
            return backup;
        }
        catch
        {
            return null;
        }
    }

    private static TriggerButton ParseButton(string? value, TriggerButton fallback) =>
        Enum.TryParse<TriggerButton>(value, ignoreCase: true, out var b) && Enum.IsDefined(b) ? b : fallback;
}
