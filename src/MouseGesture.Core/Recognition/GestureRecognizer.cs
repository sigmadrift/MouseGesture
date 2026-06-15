using MouseGesture.Core.Hooks;
using R3;

namespace MouseGesture.Core.Recognition;

/// <summary>
/// Listens to mouse events while the trigger button is held, builds a sequence of
/// 4-direction strokes, and emits a <see cref="Gesture"/> on trigger release.
/// If no gesture was made (just a click), forwards a synthesized click of the same
/// button so the app under the cursor still sees its normal behavior.
/// </summary>
public sealed class GestureRecognizer : IDisposable
{
    private readonly Subject<Gesture> _recognized = new();
    private readonly Subject<Direction> _strokeAdded = new();
    private readonly IDisposable _subscription;

    private bool _capturing;
    private (int X, int Y) _anchor;
    private (int X, int Y) _lastPoint;
    private Direction? _lastDir;
    private readonly List<Direction> _sequence = new();
    private bool _gestureStarted;
    private bool _actionFired;
    private volatile bool _isEnabled = true;
    private TriggerButton _trigger = TriggerButton.Right;

    /// <summary>
    /// Optional probe consulted after each new stroke. When it returns true, the
    /// gesture is emitted immediately and further movement is ignored for the rest
    /// of the trigger hold. When null, the gesture only fires on trigger release.
    /// </summary>
    public Func<string, bool>? EarlyRecognize { get; set; }

    /// <summary>Pixels of movement required from the last anchor before recording a direction.</summary>
    public int MinSegmentDistance { get; set; } = 30;

    /// <summary>Pixels the cursor must travel from the start before any gesture is recognized.</summary>
    public int InitialDeadZone { get; set; } = 10;

    /// <summary>The mouse button held to begin a gesture.</summary>
    public TriggerButton Trigger
    {
        get => _trigger;
        set
        {
            if (_trigger == value)
                return;
            _trigger = value;
            ResetCapture();
        }
    }

    /// <summary>
    /// When false, mouse events pass through untouched (no capture, no suppression).
    /// Setting to false also clears any in-progress capture state.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            if (!value)
                ResetCapture();
        }
    }

    public Observable<Gesture> Recognized => _recognized;

    /// <summary>Fires every time a new direction is added to the in-progress gesture (for live overlay).</summary>
    public Observable<Direction> StrokeAdded => _strokeAdded;

    public GestureRecognizer(LowLevelMouseHook hook)
    {
        _subscription = hook.Events.Subscribe(this, static (args, self) => self.OnMouseEvent(args));
    }

    private void OnMouseEvent(MouseHookEventArgs args)
    {
        if (!_isEnabled)
            return;

        var ev = args.Event;

        if (ev.Type == MouseEventType.Move)
        {
            if (_capturing)
                ProcessMove(ev.X, ev.Y);
            return;
        }

        if (IsTriggerDown(ev))
        {
            StartCapture(ev.X, ev.Y);
            args.Suppress = true;
            return;
        }

        if (_capturing && IsTriggerUp(ev))
        {
            args.Suppress = true;
            FinishCapture(ev.X, ev.Y);
        }
    }

    private bool IsTriggerDown(in MouseHookEvent ev) => _trigger switch
    {
        TriggerButton.Right => ev.Type == MouseEventType.RightDown,
        TriggerButton.Middle => ev.Type == MouseEventType.MiddleDown,
        TriggerButton.XButton1 => ev.Type == MouseEventType.XButtonDown && ev.XButton == 1,
        TriggerButton.XButton2 => ev.Type == MouseEventType.XButtonDown && ev.XButton == 2,
        _ => false,
    };

    private bool IsTriggerUp(in MouseHookEvent ev) => _trigger switch
    {
        TriggerButton.Right => ev.Type == MouseEventType.RightUp,
        TriggerButton.Middle => ev.Type == MouseEventType.MiddleUp,
        TriggerButton.XButton1 => ev.Type == MouseEventType.XButtonUp && ev.XButton == 1,
        TriggerButton.XButton2 => ev.Type == MouseEventType.XButtonUp && ev.XButton == 2,
        _ => false,
    };

    private void StartCapture(int x, int y)
    {
        _capturing = true;
        _anchor = (x, y);
        _lastPoint = _anchor;
        _sequence.Clear();
        _lastDir = null;
        _gestureStarted = false;
        _actionFired = false;
    }

    private void ResetCapture()
    {
        _capturing = false;
        _sequence.Clear();
        _lastDir = null;
        _gestureStarted = false;
        _actionFired = false;
    }

    private void ProcessMove(int x, int y)
    {
        // Once the action has fired, swallow movement until the trigger is released
        // so the user can let go of the button without triggering anything else.
        if (_actionFired)
            return;

        // Single-stroke only: once a direction is recorded, ignore further movement
        // so the recognizer doesn't accumulate a multi-direction sequence.
        if (_sequence.Count > 0)
            return;

        _lastPoint = (x, y);
        var dx = x - _anchor.X;
        var dy = y - _anchor.Y;
        var dist2 = (long)dx * dx + (long)dy * dy;
        var threshold = _gestureStarted ? MinSegmentDistance : InitialDeadZone;
        if (dist2 < (long)threshold * threshold)
            return;

        var dir = ResolveDirection(dx, dy);
        _gestureStarted = true;

        if (_lastDir != dir)
        {
            _sequence.Add(dir);
            _lastDir = dir;
            _strokeAdded.OnNext(dir);

            var probe = EarlyRecognize;
            if (probe is not null)
            {
                var gesture = new Gesture([.. _sequence]);
                if (probe(gesture.Stroke))
                {
                    _actionFired = true;
                    _recognized.OnNext(gesture);
                    return;
                }
            }
        }
        _anchor = (x, y);
    }

    private void FinishCapture(int x, int y)
    {
        _capturing = false;
        if (_actionFired)
            return;
        if (_sequence.Count == 0)
        {
            InputSimulator.ButtonClick(_trigger);
            return;
        }
        _recognized.OnNext(new Gesture([.. _sequence]));
    }

    private static Direction ResolveDirection(int dx, int dy)
    {
        // Screen coords: +y is down.
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx >= 0 ? Direction.Right : Direction.Left;
        return dy >= 0 ? Direction.Down : Direction.Up;
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _recognized.Dispose();
        _strokeAdded.Dispose();
    }
}
