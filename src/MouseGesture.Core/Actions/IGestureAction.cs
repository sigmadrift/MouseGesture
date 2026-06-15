namespace MouseGesture.Core.Actions;

public interface IGestureAction
{
    /// <summary>Stable identifier used in persisted config (do not localize).</summary>
    string Id { get; }

    /// <summary>Human-readable name for the UI.</summary>
    string Name { get; }

    void Execute();
}
