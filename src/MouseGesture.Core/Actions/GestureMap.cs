using System.Collections.Concurrent;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Actions;

public sealed class GestureMap
{
    private readonly ConcurrentDictionary<string, IGestureAction> _bindings = new();

    public void Bind(string stroke, IGestureAction action) => _bindings[stroke] = action;

    public bool Unbind(string stroke) => _bindings.TryRemove(stroke, out _);

    public IGestureAction? Resolve(Gesture gesture) =>
        _bindings.TryGetValue(gesture.Stroke, out var action) ? action : null;

    /// <summary>
    /// True when the stroke matches a binding. The recognizer uses this to fire the
    /// action the moment a stroke is recognized, without waiting for trigger release.
    /// </summary>
    public bool IsBound(string stroke) =>
        !string.IsNullOrEmpty(stroke) && _bindings.ContainsKey(stroke);

    public IReadOnlyDictionary<string, IGestureAction> Bindings => _bindings;

    public static GestureMap CreateDefault()
    {
        var map = new GestureMap();
        // 우클릭 + 드래그: ←=이전 데스크탑, →=다음 데스크탑, ↑=작업 보기, ↓=바탕 화면
        map.Bind("L", BuiltInActions.PreviousDesktop);
        map.Bind("R", BuiltInActions.NextDesktop);
        map.Bind("U", BuiltInActions.TaskView);
        map.Bind("D", BuiltInActions.ShowDesktop);
        return map;
    }
}
