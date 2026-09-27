using System.Collections.Concurrent;

namespace MouseGesture.Core.Input;

/// <summary>
/// A single dedicated thread that executes posted work strictly in order. All synthetic
/// input (button passthrough, wheel notches, gesture key chords) goes through one
/// instance so that:
/// <list type="bullet">
/// <item>SendInput never runs inside the low-level hook callback (which can stall the
/// input pipeline and get the hook removed by Windows);</item>
/// <item>injected events keep their relative order (e.g. a passthrough DOWN always
/// precedes its UP, and two quick gesture chords never interleave their modifiers).</item>
/// </list>
/// </summary>
public sealed class SerialWorkQueue : IWorkQueue, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    /// <summary>Raised on the worker thread when a work item throws.</summary>
    public event Action<Exception>? Faulted;

    public SerialWorkQueue(string name)
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public void Post(Action work)
    {
        try
        {
            _queue.Add(work);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Shutting down; drop the work.
        }
    }

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                try { Faulted?.Invoke(ex); } catch { }
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        // Only dispose the collection if the worker actually drained and exited.
        if (_thread.Join(1000))
            _queue.Dispose();
    }
}
