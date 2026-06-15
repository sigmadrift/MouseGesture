using System.Collections.Concurrent;
using MouseGesture.Core.Hooks;
using MouseGesture.Core.Native;
using R3;

namespace MouseGesture.Core.Recognition;

/// <summary>
/// While a side modifier button (the "back" X1 or "forward" X2) is held, wheel notches
/// are suppressed and re-emitted with an amplified delta so scrolling moves further per
/// notch. If the modifier is pressed and released without any wheel movement, a normal
/// click of that button is synthesized so its usual action still works.
///
/// All synthetic input (SendInput) runs on a dedicated pump thread, never on the
/// low-level hook thread: injecting input from inside the hook callback — especially at
/// wheel frequency — can stall the system input pipeline and freeze the machine. The
/// hook callback only decides suppression and queues work.
/// </summary>
public sealed class WheelAmplifier : IDisposable
{
    private const int NotCapturing = -1;

    private readonly IDisposable _subscription;

    // Pump thread: performs SendInput off the hook thread.
    private readonly Thread _pumpThread;
    private readonly AutoResetEvent _signal = new(false);
    private readonly ConcurrentQueue<TriggerButton> _pendingClicks = new();
    private int _pendingWheelDelta;
    private int _wheelRemainder; // sub-notch carry; pump-thread only
    private volatile bool _running = true;

    // Safety bound on a single batch so a starved pump can't emit a giant burst at once.
    private const int MaxNotchesPerCycle = 300;

    // Capture state (read on the hook thread, occasionally written from the UI thread).
    // Holds (int)TriggerButton of the button whose DOWN we suppressed, or NotCapturing.
    private volatile int _capturedButton = NotCapturing;
    private volatile bool _wheelUsed;

    private volatile bool _isEnabled = WheelSettings.Default.Enabled;
    private volatile TriggerButton _modifier = WheelSettings.Default.Modifier;
    private volatile int _multiplier = WheelSettings.Default.Multiplier;

    public WheelAmplifier(LowLevelMouseHook hook)
    {
        _pumpThread = new Thread(PumpLoop)
        {
            IsBackground = true,
            Name = "MouseGesture.WheelPump",
        };
        _pumpThread.Start();
        _subscription = hook.Events.Subscribe(this, static (args, self) => self.OnMouseEvent(args));
    }

    /// <summary>When false, wheel events pass through untouched and the modifier behaves normally.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>The side button held to amplify the wheel. Coerced to a valid side button.</summary>
    public TriggerButton Modifier
    {
        get => _modifier;
        set => _modifier = WheelSettings.IsValidModifier(value) ? value : WheelSettings.Default.Modifier;
    }

    /// <summary>Scroll distance multiplier applied while the modifier is held (clamped to a sane range).</summary>
    public int Multiplier
    {
        get => _multiplier;
        set => _multiplier = Math.Clamp(value, WheelSettings.MinMultiplier, WheelSettings.MaxMultiplier);
    }

    public void Apply(WheelSettings settings)
    {
        var normalized = settings.Normalized();
        Modifier = normalized.Modifier;
        Multiplier = normalized.Multiplier;
        IsEnabled = normalized.Enabled;
    }

    private void OnMouseEvent(MouseHookEventArgs args)
    {
        var ev = args.Event;
        var captured = _capturedButton;

        // 1. Balance a previously-suppressed modifier DOWN with its UP — even if the
        //    feature was just disabled or the modifier was reconfigured mid-hold —
        //    otherwise the app would see a lone UP (or a stuck button).
        if (captured != NotCapturing && IsButton(ev, (TriggerButton)captured, down: false))
        {
            _capturedButton = NotCapturing;
            args.Suppress = true;
            if (!_wheelUsed)
                EnqueueClick((TriggerButton)captured);
            _wheelUsed = false;
            return;
        }

        if (!_isEnabled)
            return;

        // 2. Start (or restart, replacing any stale capture) on modifier DOWN.
        if (IsButton(ev, _modifier, down: true))
        {
            _capturedButton = (int)_modifier;
            _wheelUsed = false;
            args.Suppress = true;
            return;
        }

        if (captured == NotCapturing)
            return;

        // 3. Wheel while the modifier is held.
        if (ev.Type == MouseEventType.Wheel)
        {
            // NOTE: we can't validate "is the button still physically down?" via
            // GetAsyncKeyState here — because we suppressed the button's DOWN, the
            // system's async key state never registered it, so the query would always
            // say "up" and defeat amplification entirely. Capture is instead cleared
            // by the modifier UP, or replaced when the modifier is pressed again.
            _wheelUsed = true;

            // At 1x there is nothing to amplify; let the wheel pass through unchanged
            // (but keep _wheelUsed set so the release doesn't fire a stray click).
            var multiplier = _multiplier;
            if (multiplier <= 1)
                return;

            args.Suppress = true;
            Interlocked.Add(ref _pendingWheelDelta, ev.WheelDelta * multiplier);
            _signal.Set();
        }
    }

    private void EnqueueClick(TriggerButton button)
    {
        _pendingClicks.Enqueue(button);
        _signal.Set();
    }

    private void PumpLoop()
    {
        while (true)
        {
            try
            {
                _signal.WaitOne();
                if (!_running)
                    return;

                // Coalesce wheel motion accumulated since the last pump, then emit it as
                // discrete WHEEL_DELTA notches (carrying any sub-notch fraction forward).
                var delta = Interlocked.Exchange(ref _pendingWheelDelta, 0) + _wheelRemainder;
                var count = Math.Abs(delta) / Win32.WHEEL_DELTA;
                if (count > 0)
                {
                    var notch = delta >= 0 ? Win32.WHEEL_DELTA : -Win32.WHEEL_DELTA;
                    var batch = Math.Min(count, MaxNotchesPerCycle);
                    InputSimulator.Wheel(notch, batch);
                    _wheelRemainder = delta - notch * batch;
                    if (count > batch)
                        _signal.Set(); // more remained than one batch; flush on the next cycle
                }
                else
                {
                    _wheelRemainder = delta;
                }

                while (_pendingClicks.TryDequeue(out var button))
                    InputSimulator.ButtonClick(button);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                // Never let a synthetic-input failure kill the pump thread/process.
            }
        }
    }

    private static bool IsButton(in MouseHookEvent ev, TriggerButton button, bool down) => button switch
    {
        TriggerButton.Right => ev.Type == (down ? MouseEventType.RightDown : MouseEventType.RightUp),
        TriggerButton.Middle => ev.Type == (down ? MouseEventType.MiddleDown : MouseEventType.MiddleUp),
        TriggerButton.XButton1 => ev.Type == (down ? MouseEventType.XButtonDown : MouseEventType.XButtonUp) && ev.XButton == 1,
        TriggerButton.XButton2 => ev.Type == (down ? MouseEventType.XButtonDown : MouseEventType.XButtonUp) && ev.XButton == 2,
        _ => false,
    };

    public void Dispose()
    {
        _subscription.Dispose();
        _running = false;
        _signal.Set();
        // Only dispose the wait handle if the pump actually exited; otherwise leave it
        // (the background thread is reclaimed at process exit) to avoid an
        // ObjectDisposedException racing inside a stuck pump.
        if (_pumpThread.Join(1000))
            _signal.Dispose();
    }
}
