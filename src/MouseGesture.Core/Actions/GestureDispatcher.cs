using MouseGesture.Core.Recognition;
using R3;

namespace MouseGesture.Core.Actions;

/// <summary>Subscribes to recognized gestures and runs the bound action.</summary>
public sealed class GestureDispatcher : IDisposable
{
    private readonly IDisposable _subscription;
    private readonly Subject<(Gesture Gesture, IGestureAction? Action)> _executed = new();

    public GestureMap Map { get; }
    public Observable<(Gesture Gesture, IGestureAction? Action)> Executed => _executed;

    public GestureDispatcher(GestureRecognizer recognizer, GestureMap map)
    {
        Map = map;
        _subscription = recognizer.Recognized.Subscribe(this, static (gesture, self) =>
        {
            var action = self.Map.Resolve(gesture);
            // Run off the hook thread: a slow Execute() (e.g. Process.Start) on the
            // low-level mouse hook thread can blow past LowLevelHooksTimeout and
            // cause Windows to silently drop the hook.
            ThreadPool.UnsafeQueueUserWorkItem(
                static state => state.self.Run(state.gesture, state.action),
                (self, gesture, action),
                preferLocal: false);
        });
    }

    private void Run(Gesture gesture, IGestureAction? action)
    {
        try
        {
            action?.Execute();
        }
        catch
        {
            // Action failures should not take the dispatcher down.
        }
        finally
        {
            _executed.OnNext((gesture, action));
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _executed.Dispose();
    }
}
