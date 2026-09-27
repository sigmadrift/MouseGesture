using System.Collections.Immutable;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Persistence;

/// <summary>Everything persisted besides the gesture map.</summary>
public sealed record AppSettings(
    TriggerButton Trigger,
    WheelSettings Wheel,
    int HoldTimeoutMs,
    bool DisableInFullscreen,
    ImmutableArray<string> ExcludedApps)
{
    public const int MinHoldTimeoutMs = 0;
    public const int MaxHoldTimeoutMs = 5000;

    public static AppSettings Default { get; } = new(
        TriggerButton.Right,
        WheelSettings.Default,
        HoldTimeoutMs: 500,
        DisableInFullscreen: true,
        ExcludedApps: []);

    /// <summary>Clamps ranges and normalizes/dedups the excluded process names.</summary>
    public AppSettings Normalized() => this with
    {
        Trigger = Enum.IsDefined(Trigger) ? Trigger : TriggerButton.Right,
        Wheel = Wheel.Normalized(),
        HoldTimeoutMs = Math.Clamp(HoldTimeoutMs, MinHoldTimeoutMs, MaxHoldTimeoutMs),
        ExcludedApps = [.. (ExcludedApps.IsDefault ? [] : ExcludedApps)
            .Select(ScreenProbe.NormalizeProcessName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)],
    };

    /// <summary>
    /// Wheel amplification actually running: configured on and not sharing the gesture
    /// trigger button (both features would fight over the same button).
    /// </summary>
    public bool IsWheelEffective => Wheel.Enabled && Wheel.Modifier != Trigger;
}
