namespace MouseGesture.Core.Input;

/// <summary>Runs posted work items in FIFO order, off the caller's thread.</summary>
public interface IWorkQueue
{
    /// <summary>Queues <paramref name="work"/>. Never blocks on the work itself; safe to call from the hook thread.</summary>
    void Post(Action work);
}
