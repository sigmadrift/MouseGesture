using R3;

namespace MouseGesture.Core.Hooks;

/// <summary>
/// Stream of global mouse events. Subscribers run synchronously on the hook thread and
/// may set <see cref="MouseHookEventArgs.Suppress"/>; they must return quickly.
/// </summary>
public interface IMouseEventSource
{
    Observable<MouseHookEventArgs> Events { get; }
}
