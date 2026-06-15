using System.Text.Json;
using MouseGesture.Core.Actions;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Persistence;

/// <summary>Loads and saves gestures + trigger button as JSON in %APPDATA%/MouseGesture.</summary>
public sealed class BindingStore
{
    private readonly string _path;
    private readonly ActionRegistry _registry;

    public BindingStore(ActionRegistry registry, string? overridePath = null)
    {
        _registry = registry;
        _path = overridePath ?? DefaultPath();
    }

    public static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MouseGesture");
        return Path.Combine(dir, "bindings.json");
    }

    /// <summary>Reads and parses the file, returning null if missing or unreadable.</summary>
    private BindingsConfig? TryReadFile()
    {
        if (!File.Exists(_path))
            return null;
        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, BindingsJsonContext.Default.BindingsConfig);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the persisted map, or the default map if no file exists or it can't be read.</summary>
    public GestureMap LoadOrDefault()
    {
        var config = TryReadFile();
        if (config is null)
            return GestureMap.CreateDefault();

        var map = new GestureMap();
        foreach (var entry in config.Bindings)
        {
            if (string.IsNullOrEmpty(entry.Stroke))
                continue;
            var action = _registry.Get(entry.ActionId);
            if (action is null)
                continue;
            map.Bind(entry.Stroke, action);
        }
        return map;
    }

    /// <summary>Returns the saved trigger button (default <see cref="TriggerButton.Right"/>).</summary>
    public TriggerButton LoadTrigger()
    {
        var config = TryReadFile();
        if (config is null)
            return TriggerButton.Right;
        return Enum.TryParse<TriggerButton>(config.Trigger, ignoreCase: true, out var t)
            ? t
            : TriggerButton.Right;
    }

    /// <summary>Returns the saved wheel-amplification settings (or defaults).</summary>
    public WheelSettings LoadWheelSettings()
    {
        var config = TryReadFile();
        if (config is null)
            return WheelSettings.Default;
        var modifier = Enum.TryParse<TriggerButton>(config.WheelModifierButton, ignoreCase: true, out var m)
            ? m
            : WheelSettings.Default.Modifier;
        return new WheelSettings(config.WheelAmplifyEnabled, modifier, config.WheelMultiplier).Normalized();
    }

    /// <summary>Saves the map, trigger, and wheel settings atomically.</summary>
    public void Save(GestureMap map, TriggerButton trigger, WheelSettings wheel)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var normalized = wheel.Normalized();
        var config = new BindingsConfig
        {
            Trigger = trigger.ToString(),
            Bindings = map.Bindings
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new BindingEntry { Stroke = p.Key, ActionId = p.Value.Id })
                .ToList(),
            WheelAmplifyEnabled = normalized.Enabled,
            WheelModifierButton = normalized.Modifier.ToString(),
            WheelMultiplier = normalized.Multiplier,
        };

        var tmp = _path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, config, BindingsJsonContext.Default.BindingsConfig);
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>Saves the map and trigger, preserving the previously saved wheel settings.</summary>
    public void Save(GestureMap map, TriggerButton trigger) => Save(map, trigger, LoadWheelSettings());

    /// <summary>Saves the map preserving the previously saved trigger and wheel settings.</summary>
    public void Save(GestureMap map) => Save(map, LoadTrigger());
}
