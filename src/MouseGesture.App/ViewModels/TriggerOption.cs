using MouseGesture.Core.Recognition;

namespace MouseGesture.App.ViewModels;

public sealed record TriggerOption(TriggerButton Value, string Display)
{
    public static IReadOnlyList<TriggerOption> All { get; } =
    [
        new(TriggerButton.Right,    "오른쪽 버튼 (기본)"),
        new(TriggerButton.Middle,   "가운데 버튼 (휠 클릭)"),
        new(TriggerButton.XButton1, "X1 버튼 (보통 '뒤로')"),
        new(TriggerButton.XButton2, "X2 버튼 (보통 '앞으로')"),
    ];

    /// <summary>
    /// Buttons allowed as the wheel-amplification modifier. Restricted to the side
    /// buttons (X1/X2) so the feature can't break hold/drag of Right/Middle.
    /// </summary>
    public static IReadOnlyList<TriggerOption> WheelModifiers { get; } =
        All.Where(o => o.Value is TriggerButton.XButton1 or TriggerButton.XButton2).ToArray();

    public static TriggerOption ForValue(TriggerButton value)
        => All.FirstOrDefault(o => o.Value == value) ?? All[0];
}
