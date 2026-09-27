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

    public IGestureAction? Get(string stroke) => _bindings.GetValueOrDefault(stroke);

    /// <summary>True when the stroke matches a binding.</summary>
    public bool IsBound(string stroke) =>
        !string.IsNullOrEmpty(stroke) && _bindings.ContainsKey(stroke);

    /// <summary>True when some binding is longer than <paramref name="prefix"/> and starts with it.</summary>
    public bool HasLongerBinding(string prefix)
    {
        foreach (var key in _bindings.Keys)
        {
            if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the stroke is bound and no longer binding extends it, so the recognizer
    /// can fire the action the moment the stroke is drawn instead of waiting for release.
    /// (With "L" and "LU" both bound, "L" must wait: the user may still be heading for "LU".)
    /// </summary>
    public bool CanFireEarly(string stroke) => IsBound(stroke) && !HasLongerBinding(stroke);

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
