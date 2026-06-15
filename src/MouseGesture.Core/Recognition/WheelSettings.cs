namespace MouseGesture.Core.Recognition;

/// <summary>
/// Configuration for wheel amplification: while <see cref="Modifier"/> is held,
/// each physical wheel notch is re-emitted <see cref="Multiplier"/> times.
/// </summary>
public sealed record WheelSettings(bool Enabled, TriggerButton Modifier, int Multiplier)
{
    public const int MinMultiplier = 1;
    public const int MaxMultiplier = 20;

    public static WheelSettings Default { get; } = new(true, TriggerButton.XButton1, 3);

    /// <summary>
    /// Only the side buttons may modify the wheel. Right/Middle are rejected because
    /// suppressing them to capture a scroll would break their hold/drag behavior
    /// (e.g. middle-button autoscroll, right-drag), and Right is normally the trigger.
    /// </summary>
    public static bool IsValidModifier(TriggerButton button) =>
        button is TriggerButton.XButton1 or TriggerButton.XButton2;

    /// <summary>Returns a copy with the multiplier clamped and the modifier coerced to a valid side button.</summary>
    public WheelSettings Normalized() => this with
    {
        Multiplier = Math.Clamp(Multiplier, MinMultiplier, MaxMultiplier),
        Modifier = IsValidModifier(Modifier) ? Modifier : Default.Modifier,
    };
}
