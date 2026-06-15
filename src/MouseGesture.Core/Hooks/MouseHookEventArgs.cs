namespace MouseGesture.Core.Hooks;

public sealed class MouseHookEventArgs
{
    public MouseHookEvent Event { get; internal set; }

    /// <summary>
    /// Set true within an event handler to swallow the event so it does not
    /// reach other applications. Used to suppress right-click during gestures.
    /// </summary>
    public bool Suppress { get; set; }
}
