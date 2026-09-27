using MouseGesture.Core.Hooks;
using MouseGesture.Core.Input;
using R3;

namespace MouseGesture.Core.Recognition;

/// <summary>
/// Listens to mouse events while the trigger button is held and builds a sequence of
/// 4-direction strokes (e.g. "URD"). The trigger DOWN is swallowed; on release:
/// <list type="bullet">
/// <item>if strokes were recorded, a <see cref="Gesture"/> is emitted;</item>
/// <item>if not (a plain or slightly shaky click), a synthesized click of the same
/// button is sent so the app under the cursor still sees its normal behavior.</item>
/// </list>
/// If the button is held without moving for <see cref="HoldTimeoutMs"/>, the hold is
/// handed back to the system (a synthesized DOWN, and later UP) so press-and-hold uses
/// such as right-drag keep working.
///
/// All state changes happen under a lock shared by the hook thread, the hold timer and
/// the UI thread (settings); synthetic input is posted to an <see cref="IWorkQueue"/>.
/// </summary>
public sealed class GestureRecognizer : IDisposable
{
    private enum HoldState
    {
        /// <summary>No button of ours is held.</summary>
        Idle,
        /// <summary>Trigger DOWN swallowed; recording strokes.</summary>
        Capturing,
        /// <summary>Hold handed back: synthetic DOWN sent, the real UP will be replaced by a synthetic UP.</summary>
        PassThrough,
        /// <summary>Capture abandoned (paused / trigger changed mid-hold); swallow the pending UP.</summary>
        Orphaned,
    }

    private readonly object _gate = new();
    private readonly Subject<Gesture> _recognized = new();
    private readonly Subject<Direction> _strokeAdded = new();
    private readonly IDisposable _subscription;
    private readonly IWorkQueue _queue;
    private readonly IInputSynthesizer _input;
    private readonly TimeProvider _time;
    private readonly ITimer _holdTimer;

    private HoldState _state;
    private TriggerButton _heldButton;
    private long _holdArmedAt;
    private (int X, int Y) _origin;
    private (int X, int Y) _anchor;
    private double _scale = 1;
    private Direction? _lastDir;
    private readonly List<Direction> _sequence = new();
    private bool _gestureStarted;
    private bool _actionFired;
    private bool _isEnabled = true;
    private TriggerButton _trigger = TriggerButton.Right;

    public GestureRecognizer(IMouseEventSource source, IWorkQueue queue, IInputSynthesizer input, TimeProvider? time = null)
    {
        _queue = queue;
        _input = input;
        _time = time ?? TimeProvider.System;
        _holdTimer = _time.CreateTimer(static s => ((GestureRecognizer)s!).OnHoldTimeout(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _subscription = source.Events.Subscribe(this, static (args, self) => self.OnMouseEvent(args));
    }

    /// <summary>
    /// Optional probe consulted after each new stroke. When it returns true, the gesture
    /// is emitted immediately and further movement is ignored for the rest of the hold.
    /// Otherwise the gesture fires on trigger release. It should return true only when no
    /// longer binding could still be reached (see <c>GestureMap.CanFireEarly</c>).
    /// </summary>
    public Func<string, bool>? EarlyRecognize { get; set; }

    /// <summary>
    /// Optional probe consulted on trigger DOWN with the cursor position. Returning true
    /// leaves the press completely untouched (excluded apps, fullscreen games, …).
    /// </summary>
    public Func<int, int, bool>? ShouldBypass { get; set; }

    /// <summary>Optional DPI scale (1.0 = 96 DPI) at a screen point; distances are multiplied by it.</summary>
    public Func<int, int, double>? DpiScale { get; set; }

    /// <summary>Pixels (at 96 DPI) of movement from the last anchor before recording a direction.</summary>
    public int MinSegmentDistance { get; set; } = 30;

    /// <summary>
    /// Pixels (at 96 DPI) the cursor must travel from the press point before the press
    /// counts as "moving" (which cancels the hold timeout).
    /// </summary>
    public int InitialDeadZone { get; set; } = 10;

    /// <summary>
    /// Milliseconds the trigger may be held without moving before the hold is passed
    /// through to the app as a normal press. 0 disables the passthrough.
    /// </summary>
    public int HoldTimeoutMs { get; set; } = 500;

    /// <summary>The mouse button held to begin a gesture.</summary>
    public TriggerButton Trigger
    {
        get { lock (_gate) return _trigger; }
        set
        {
            lock (_gate)
            {
                if (_trigger == value)
                    return;
                _trigger = value;
                AbandonCapture();
            }
        }
    }

    /// <summary>
    /// When false, mouse events pass through untouched (no capture, no suppression).
    /// Setting to false abandons any in-progress capture while keeping DOWN/UP balanced.
    /// </summary>
    public bool IsEnabled
    {
        get { lock (_gate) return _isEnabled; }
        set
        {
            lock (_gate)
            {
                if (_isEnabled == value)
                    return;
                _isEnabled = value;
                if (!value)
                    AbandonCapture();
            }
        }
    }

    public Observable<Gesture> Recognized => _recognized;

    /// <summary>Fires every time a new direction is added to the in-progress gesture (for live overlay).</summary>
    public Observable<Direction> StrokeAdded => _strokeAdded;

    private void OnMouseEvent(MouseHookEventArgs args)
    {
        var ev = args.Event;
        Direction? added = null;
        Gesture? recognized = null;

        lock (_gate)
        {
            // The UP of a button we intercepted is always handled — even when paused or
            // after the trigger changed — so the app never sees an unbalanced DOWN/UP.
            if (_state != HoldState.Idle && _heldButton.Matches(ev, down: false))
            {
                args.Suppress = true;
                recognized = Release();
            }
            else if (!_isEnabled)
            {
                return;
            }
            else if (ev.Type == MouseEventType.Move)
            {
                if (_state == HoldState.Capturing)
                    ProcessMove(ev.X, ev.Y, ref added, ref recognized);
            }
            else if (_trigger.Matches(ev, down: true))
            {
                if (_state != HoldState.Idle)
                    ResetStaleHold(); // a previous UP was lost (e.g. secure desktop)
                if (ShouldBypass?.Invoke(ev.X, ev.Y) == true)
                    return;
                StartCapture(ev.X, ev.Y);
                args.Suppress = true;
            }
        }

        // Raise outside the lock: subscribers must not be able to deadlock the hook.
        if (added is { } dir)
            _strokeAdded.OnNext(dir);
        if (recognized is not null)
            _recognized.OnNext(recognized);
    }

    private void StartCapture(int x, int y)
    {
        _state = HoldState.Capturing;
        _heldButton = _trigger;
        _origin = (x, y);
        _anchor = (x, y);
        _sequence.Clear();
        _lastDir = null;
        _gestureStarted = false;
        _actionFired = false;
        _scale = Math.Max(0.5, DpiScale?.Invoke(x, y) ?? 1.0);

        var timeout = HoldTimeoutMs;
        if (timeout > 0)
        {
            _holdArmedAt = _time.GetTimestamp();
            _holdTimer.Change(TimeSpan.FromMilliseconds(timeout), Timeout.InfiniteTimeSpan);
        }
    }

    private void ProcessMove(int x, int y, ref Direction? added, ref Gesture? recognized)
    {
        // Once the action has fired, swallow movement until the trigger is released
        // so the user can let go of the button without triggering anything else.
        if (_actionFired)
            return;

        if (!_gestureStarted)
        {
            if (Distance2(x - _origin.X, y - _origin.Y) < Square(Scaled(InitialDeadZone)))
                return;
            _gestureStarted = true;
            StopHoldTimer();
        }

        if (_sequence.Count >= StrokeFormat.MaxLength)
            return;

        var dx = x - _anchor.X;
        var dy = y - _anchor.Y;
        if (Distance2(dx, dy) < Square(Scaled(MinSegmentDistance)))
            return;

        _anchor = (x, y);
        var dir = ResolveDirection(dx, dy);
        if (_lastDir == dir)
            return;

        _sequence.Add(dir);
        _lastDir = dir;
        added = dir;

        var probe = EarlyRecognize;
        if (probe is null)
            return;
        var gesture = new Gesture([.. _sequence]);
        if (probe(gesture.Stroke))
        {
            _actionFired = true;
            recognized = gesture;
        }
    }

    /// <summary>Handles the UP of the held button. Returns the gesture to emit, if any.</summary>
    private Gesture? Release()
    {
        var state = _state;
        var button = _heldButton;
        _state = HoldState.Idle;
        StopHoldTimer();

        switch (state)
        {
            case HoldState.PassThrough:
                _queue.Post(() => _input.ButtonUp(button));
                return null;
            case HoldState.Capturing when !_actionFired:
                if (_sequence.Count == 0)
                {
                    _queue.Post(() => _input.ButtonClick(button));
                    return null;
                }
                return new Gesture([.. _sequence]);
            default:
                return null;
        }
    }

    private void OnHoldTimeout()
    {
        lock (_gate)
        {
            if (_state != HoldState.Capturing || _gestureStarted || _actionFired)
                return;
            // Ignore a stale tick from an earlier hold whose timer was re-armed.
            var timeout = HoldTimeoutMs;
            if (timeout <= 0 || _time.GetElapsedTime(_holdArmedAt).TotalMilliseconds < timeout - 1)
                return;

            _state = HoldState.PassThrough;
            var button = _heldButton;
            _queue.Post(() => _input.ButtonDown(button));
        }
    }

    /// <summary>Stops capturing but keeps tracking the held button so its UP stays balanced.</summary>
    private void AbandonCapture()
    {
        if (_state == HoldState.Capturing)
            _state = HoldState.Orphaned;
        _sequence.Clear();
        _lastDir = null;
        StopHoldTimer();
    }

    private void ResetStaleHold()
    {
        if (_state == HoldState.PassThrough)
        {
            var button = _heldButton;
            _queue.Post(() => _input.ButtonUp(button));
        }
        _state = HoldState.Idle;
        StopHoldTimer();
    }

    private void StopHoldTimer() => _holdTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private int Scaled(int px) => (int)Math.Round(px * _scale);

    private static long Distance2(int dx, int dy) => (long)dx * dx + (long)dy * dy;

    private static long Square(int v) => (long)v * v;

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
        _holdTimer.Dispose();
        _recognized.Dispose();
        _strokeAdded.Dispose();
    }
}
