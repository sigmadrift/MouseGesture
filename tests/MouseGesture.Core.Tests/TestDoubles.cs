using MouseGesture.Core.Hooks;
using MouseGesture.Core.Input;
using MouseGesture.Core.Recognition;
using R3;

namespace MouseGesture.Core.Tests;

internal sealed class FakeMouseSource : IMouseEventSource
{
    private readonly Subject<MouseHookEventArgs> _events = new();

    public Observable<MouseHookEventArgs> Events => _events;

    /// <summary>Sends an event through subscribers; returns whether it was suppressed.</summary>
    public bool Send(MouseEventType type, int x = 0, int y = 0, int wheelDelta = 0, int xButton = 0)
    {
        var args = new MouseHookEventArgs { Event = new MouseHookEvent(type, x, y, wheelDelta, xButton, 0) };
        _events.OnNext(args);
        return args.Suppress;
    }

    public bool Move(int x, int y) => Send(MouseEventType.Move, x, y);
}

/// <summary>Runs posted work immediately on the caller's thread.</summary>
internal sealed class ImmediateQueue : IWorkQueue
{
    public void Post(Action work) => work();
}

internal sealed class RecordingSynthesizer : IInputSynthesizer
{
    public List<string> Calls { get; } = new();

    public void ButtonDown(TriggerButton button) => Calls.Add($"down:{button}");
    public void ButtonUp(TriggerButton button) => Calls.Add($"up:{button}");
    public void ButtonClick(TriggerButton button) => Calls.Add($"click:{button}");
    public void Wheel(int notchDelta, int count, bool horizontal) => Calls.Add($"{(horizontal ? "hwheel" : "wheel")}:{notchDelta}x{count}");
}

/// <summary>Deterministic TimeProvider: time only moves via <see cref="Advance"/>.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = new();
    private long _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by.Ticks;
        foreach (var t in _timers.ToArray())
            t.FireIfDue(_now);
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private long? _dueAt;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime.Ticks;
            return true;
        }

        public void FireIfDue(long now)
        {
            if (_dueAt is { } due && due <= now)
            {
                _dueAt = null;
                callback(state);
            }
        }

        public void Dispose() => _dueAt = null;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
