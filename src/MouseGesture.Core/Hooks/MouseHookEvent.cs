namespace MouseGesture.Core.Hooks;

public enum MouseEventType
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    XButtonDown,
    XButtonUp,
    Wheel,
    HWheel,
}

public readonly record struct MouseHookEvent(
    MouseEventType Type,
    int X,
    int Y,
    int WheelDelta,
    int XButton,
    uint TimestampMs)
{
    public bool IsButtonEvent => Type is not MouseEventType.Move and not MouseEventType.Wheel and not MouseEventType.HWheel;
}
