using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Input;

/// <summary>
/// Injects synthetic mouse input. Implementations must mark injected events so the
/// app's own hook ignores them. Call only from an <see cref="IWorkQueue"/>, never from
/// inside the hook callback.
/// </summary>
public interface IInputSynthesizer
{
    void ButtonDown(TriggerButton button);
    void ButtonUp(TriggerButton button);

    /// <summary>A full down+up click, injected atomically.</summary>
    void ButtonClick(TriggerButton button);

    /// <summary>Sends <paramref name="count"/> discrete wheel events of <paramref name="notchDelta"/> each.</summary>
    void Wheel(int notchDelta, int count, bool horizontal);
}
