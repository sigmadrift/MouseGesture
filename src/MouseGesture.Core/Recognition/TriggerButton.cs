using MouseGesture.Core.Hooks;

namespace MouseGesture.Core.Recognition;

/// <summary>The mouse button that the user holds while drawing a gesture.</summary>
public enum TriggerButton
{
    Right,
    Middle,
    XButton1,
    XButton2,
}

public static class TriggerButtonExtensions
{
    /// <summary>True when <paramref name="ev"/> is the DOWN (or UP) of <paramref name="button"/>.</summary>
    public static bool Matches(this TriggerButton button, in MouseHookEvent ev, bool down) => button switch
    {
        TriggerButton.Right => ev.Type == (down ? MouseEventType.RightDown : MouseEventType.RightUp),
        TriggerButton.Middle => ev.Type == (down ? MouseEventType.MiddleDown : MouseEventType.MiddleUp),
        TriggerButton.XButton1 => ev.Type == (down ? MouseEventType.XButtonDown : MouseEventType.XButtonUp) && ev.XButton == 1,
        TriggerButton.XButton2 => ev.Type == (down ? MouseEventType.XButtonDown : MouseEventType.XButtonUp) && ev.XButton == 2,
        _ => false,
    };

    /// <summary>The other side button (X1 ↔ X2); other buttons map to X1.</summary>
    public static TriggerButton OtherSideButton(this TriggerButton button)
        => button == TriggerButton.XButton1 ? TriggerButton.XButton2 : TriggerButton.XButton1;
}
