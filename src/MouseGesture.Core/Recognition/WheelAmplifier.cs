using MouseGesture.Core.Hooks;
using MouseGesture.Core.Input;
using MouseGesture.Core.Native;
using R3;

namespace MouseGesture.Core.Recognition;

/// <summary>
/// While a side modifier button (the "back" X1 or "forward" X2) is held, wheel notches
/// (vertical and horizontal) are suppressed and re-emitted with an amplified delta so
/// scrolling moves further per notch.
/// <list type="bullet">
/// <item>Pressed and released without wheel movement → a normal click of that button is
/// synthesized so its usual action (e.g. browser back) still works.</item>
/// <item>Held without wheel movement for <see cref="HoldTimeoutMs"/> → the hold is handed
/// back to the system as a normal press, so push-to-talk and similar bindings work.</item>
/// </list>
///
/// The hook callback only decides suppression and queues work; all synthetic input runs
/// on the shared <see cref="IWorkQueue"/>, never on the hook thread.
/// </summary>
public sealed class WheelAmplifier : IDisposable
{
    private enum HoldState
    {
        Idle,
        Capturing,
        PassThrough,
    }

    // Notches emitted per queue turn (each costs ~1 ms, see Win32InputSynthesizer.Wheel).
    // Anything beyond is re-queued so clicks and gesture chords can interleave.
    private const int MaxNotchesPerCycle = WheelSettings.MaxMultiplier;

    private readonly object _gate = new();
    private readonly IDisposable _subscription;
    private readonly IWorkQueue _queue;
    private readonly IInputSynthesizer _input;
    private readonly TimeProvider _time;
    private readonly ITimer _holdTimer;
    private readonly Action _flush;

    private HoldState _state;
    private TriggerButton _heldButton;
    private bool _wheelUsed;
    private long _holdArmedAt;

    // Accumulated wheel delta, written on the hook thread and drained on the queue thread.
    private int _pendingVertical;
    private int _pendingHorizontal;
    private int _flushScheduled;
    // Sub-notch carry; queue thread only.
    private int _remainderVertical;
    private int _remainderHorizontal;

    private bool _isEnabled = WheelSettings.Default.Enabled;
    private TriggerButton _modifier = WheelSettings.Default.Modifier;
    private int _multiplier = WheelSettings.Default.Multiplier;

    public WheelAmplifier(IMouseEventSource source, IWorkQueue queue, IInputSynthesizer input, TimeProvider? time = null)
    {
        _queue = queue;
        _input = input;
        _time = time ?? TimeProvider.System;
        _flush = Flush;
        _holdTimer = _time.CreateTimer(static s => ((WheelAmplifier)s!).OnHoldTimeout(), this,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _subscription = source.Events.Subscribe(this, static (args, self) => self.OnMouseEvent(args));
    }

    /// <summary>When false, wheel events pass through untouched and the modifier behaves normally.</summary>
    public bool IsEnabled
    {
        get { lock (_gate) return _isEnabled; }
        set { lock (_gate) _isEnabled = value; }
    }

    /// <summary>The side button held to amplify the wheel. Coerced to a valid side button.</summary>
    public TriggerButton Modifier
    {
        get { lock (_gate) return _modifier; }
        set { lock (_gate) _modifier = WheelSettings.IsValidModifier(value) ? value : WheelSettings.Default.Modifier; }
    }

    /// <summary>Scroll distance multiplier applied while the modifier is held (clamped to a sane range).</summary>
    public int Multiplier
    {
        get { lock (_gate) return _multiplier; }
        set { lock (_gate) _multiplier = Math.Clamp(value, WheelSettings.MinMultiplier, WheelSettings.MaxMultiplier); }
    }

    /// <summary>Same meaning as <see cref="GestureRecognizer.HoldTimeoutMs"/>; 0 disables the passthrough.</summary>
    public int HoldTimeoutMs { get; set; } = 500;

    /// <summary>Optional probe on modifier DOWN; returning true leaves the press untouched.</summary>
    public Func<int, int, bool>? ShouldBypass { get; set; }

    public void Apply(WheelSettings settings)
    {
        var normalized = settings.Normalized();
        lock (_gate)
        {
            _modifier = normalized.Modifier;
            _multiplier = normalized.Multiplier;
            _isEnabled = normalized.Enabled;
        }
    }

    private void OnMouseEvent(MouseHookEventArgs args)
    {
        var ev = args.Event;
        lock (_gate)
        {
            // 1. Balance a previously-suppressed modifier DOWN with its UP — even if the
            //    feature was just disabled or the modifier was reconfigured mid-hold —
            //    otherwise the app would see a lone UP (or a stuck button).
            if (_state != HoldState.Idle && _heldButton.Matches(ev, down: false))
            {
                args.Suppress = true;
                var button = _heldButton;
                var state = _state;
                _state = HoldState.Idle;
                StopHoldTimer();
                if (state == HoldState.PassThrough)
                    _queue.Post(() => _input.ButtonUp(button));
                else if (!_wheelUsed)
                    _queue.Post(() => _input.ButtonClick(button));
                return;
            }

            // At 1x there is nothing to amplify, so don't hijack the button at all.
            if (!_isEnabled || _multiplier <= 1)
                return;

            // 2. Start (or restart, replacing any stale capture) on modifier DOWN.
            if (_modifier.Matches(ev, down: true))
            {
                if (_state == HoldState.PassThrough)
                {
                    var stale = _heldButton;
                    _queue.Post(() => _input.ButtonUp(stale));
                }
                _state = HoldState.Idle;
                StopHoldTimer();
                if (ShouldBypass?.Invoke(ev.X, ev.Y) == true)
                    return;

                _state = HoldState.Capturing;
                _heldButton = _modifier;
                _wheelUsed = false;
                args.Suppress = true;
                // Drop any sub-notch fraction left from a previous hold (FIFO: runs after
                // any flush already queued for it).
                _queue.Post(() => _remainderVertical = _remainderHorizontal = 0);

                var timeout = HoldTimeoutMs;
                if (timeout > 0)
                {
                    _holdArmedAt = _time.GetTimestamp();
                    _holdTimer.Change(TimeSpan.FromMilliseconds(timeout), Timeout.InfiniteTimeSpan);
                }
                return;
            }

            // 3. Wheel while the modifier is held.
            if (_state == HoldState.Capturing && ev.Type is MouseEventType.Wheel or MouseEventType.HWheel)
            {
                // NOTE: we can't validate "is the button still physically down?" via
                // GetAsyncKeyState here — because we suppressed the button's DOWN, the
                // system's async key state never registered it. Capture is instead cleared
                // by the modifier UP, or replaced when the modifier is pressed again.
                _wheelUsed = true;
                StopHoldTimer();
                args.Suppress = true;
                var amount = ev.WheelDelta * _multiplier;
                if (ev.Type == MouseEventType.Wheel)
                    Interlocked.Add(ref _pendingVertical, amount);
                else
                    Interlocked.Add(ref _pendingHorizontal, amount);
                ScheduleFlush();
            }
        }
    }

    private void OnHoldTimeout()
    {
        lock (_gate)
        {
            if (_state != HoldState.Capturing || _wheelUsed)
                return;
            var timeout = HoldTimeoutMs;
            if (timeout <= 0 || _time.GetElapsedTime(_holdArmedAt).TotalMilliseconds < timeout - 1)
                return;

            _state = HoldState.PassThrough;
            var button = _heldButton;
            _queue.Post(() => _input.ButtonDown(button));
        }
    }

    private void StopHoldTimer() => _holdTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private void ScheduleFlush()
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
            _queue.Post(_flush);
    }

    // Queue thread: coalesce wheel motion accumulated since the last flush and emit it as
    // discrete WHEEL_DELTA notches, carrying any sub-notch fraction forward.
    private void Flush()
    {
        Volatile.Write(ref _flushScheduled, 0);
        var more = FlushAxis(ref _pendingVertical, ref _remainderVertical, horizontal: false);
        more |= FlushAxis(ref _pendingHorizontal, ref _remainderHorizontal, horizontal: true);
        if (more)
            ScheduleFlush();
    }

    private bool FlushAxis(ref int pending, ref int remainder, bool horizontal)
    {
        var delta = Interlocked.Exchange(ref pending, 0) + remainder;
        var count = Math.Abs(delta) / Win32.WHEEL_DELTA;
        if (count == 0)
        {
            remainder = delta;
            return false;
        }

        var notch = delta >= 0 ? Win32.WHEEL_DELTA : -Win32.WHEEL_DELTA;
        var batch = Math.Min(count, MaxNotchesPerCycle);
        remainder = delta - notch * batch;
        _input.Wheel(notch, batch, horizontal);
        return count > batch;
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _holdTimer.Dispose();
    }
}
