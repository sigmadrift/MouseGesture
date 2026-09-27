using MouseGesture.Core.Input;
using MouseGesture.Core.Recognition;
using R3;

namespace MouseGesture.Core.Actions;

/// <summary>Outcome of a recognized gesture: the bound action (if any) and its failure (if any).</summary>
public sealed record GestureExecution(Gesture Gesture, IGestureAction? Action, Exception? Error);

/// <summary>Subscribes to recognized gestures and runs the bound action.</summary>
public sealed class GestureDispatcher : IDisposable
{
    private readonly IDisposable _subscription;
    private readonly IWorkQueue _queue;
    private readonly Subject<GestureExecution> _executed = new();
    private volatile bool _disposed;

    public GestureMap Map { get; }

    /// <summary>Raised on the work-queue thread after each gesture (bound or not).</summary>
    public Observable<GestureExecution> Executed => _executed;

    public GestureDispatcher(GestureRecognizer recognizer, GestureMap map, IWorkQueue queue)
    {
        Map = map;
        _queue = queue;
        _subscription = recognizer.Recognized.Subscribe(this, static (gesture, self) =>
        {
            var action = self.Map.Resolve(gesture);
            // Run off the hook thread (a slow Execute() there can blow past
            // LowLevelHooksTimeout), serialized with all other synthetic input so two
            // quick chords never interleave their modifier keys.
            self._queue.Post(() => self.Run(gesture, action));
        });
    }

    private void Run(Gesture gesture, IGestureAction? action)
    {
        Exception? error = null;
        try
        {
            action?.Execute();
        }
        catch (Exception ex)
        {
            // Action failures should not take the dispatcher down; they are reported.
            error = ex;
        }

        if (_disposed)
            return;
        try
        {
            _executed.OnNext(new GestureExecution(gesture, action, error));
        }
        catch (ObjectDisposedException)
        {
            // Raced with shutdown.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _subscription.Dispose();
        _executed.Dispose();
    }
}
