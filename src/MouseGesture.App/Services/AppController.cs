using Avalonia.Threading;
using MouseGesture.Core.Actions;
using MouseGesture.Core.Persistence;
using MouseGesture.Core.Recognition;

namespace MouseGesture.App.Services;

/// <summary>
/// Single owner of the app's user-facing state (settings, pause, autostart, bindings).
/// The tray menu and the settings window both read from and write through this class,
/// so they can never disagree, and the runtime components (recognizer, wheel amplifier,
/// app filter) are always derived from one place.
///
/// UI thread only. Saves are debounced so e.g. typing in a number box doesn't hit the disk
/// on every keystroke; <see cref="FlushSave"/> forces pending changes out (on exit).
/// </summary>
public sealed class AppController
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly GestureRecognizer _recognizer;
    private readonly WheelAmplifier _wheel;
    private readonly ScreenProbe _probe;
    private readonly BindingStore _store;
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    public AppController(
        GestureMap map,
        AppSettings settings,
        ActionRegistry registry,
        BindingStore store,
        GestureRecognizer recognizer,
        WheelAmplifier wheel,
        ScreenProbe probe)
    {
        Map = map;
        Settings = settings.Normalized();
        Registry = registry;
        _store = store;
        _recognizer = recognizer;
        _wheel = wheel;
        _probe = probe;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => FlushSave();

        if (Settings.Wheel.Enabled && !Settings.IsWheelEffective)
            Logger.Warn($"Wheel modifier ({Settings.Wheel.Modifier}) equals trigger; wheel amplification inactive.");
        ApplyRuntime();
    }

    public GestureMap Map { get; }
    public ActionRegistry Registry { get; }
    public AppSettings Settings { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsAutoStartEnabled { get; private set; }
    public bool IsAutoStartBusy { get; private set; }

    /// <summary>Settings, pause or autostart state changed.</summary>
    public event Action? StateChanged;

    public event Action? BindingsChanged;

    /// <summary>A message the user should see (conflict resolved, save failed, …).</summary>
    public event Action<string>? Notice;

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused)
            return;
        IsPaused = paused;
        ApplyRuntime();
        Logger.Info($"Pause: {paused}");
        StateChanged?.Invoke();
    }

    public void SetTrigger(TriggerButton trigger)
    {
        if (Settings.Trigger == trigger)
            return;
        var next = Settings with { Trigger = trigger };
        if (next.Wheel.Enabled && next.Wheel.Modifier == trigger)
        {
            // Keep both features working: move the wheel modifier to the other side button.
            var other = trigger.OtherSideButton();
            next = next with { Wheel = next.Wheel with { Modifier = other } };
            Notice?.Invoke($"트리거와 겹쳐서 휠 증폭 버튼을 {ButtonName(other)}(으)로 바꿨습니다.");
        }
        Update(next);
    }

    public void SetWheelEnabled(bool enabled)
    {
        if (Settings.Wheel.Enabled == enabled)
            return;
        var wheel = Settings.Wheel with { Enabled = enabled };
        if (enabled && wheel.Modifier == Settings.Trigger)
        {
            wheel = wheel with { Modifier = Settings.Trigger.OtherSideButton() };
            Notice?.Invoke($"트리거와 겹쳐서 휠 증폭 버튼을 {ButtonName(wheel.Modifier)}(으)로 바꿨습니다.");
        }
        Update(Settings with { Wheel = wheel });
    }

    /// <summary>Returns false (and raises <see cref="Notice"/>) when the button is the gesture trigger.</summary>
    public bool SetWheelModifier(TriggerButton modifier)
    {
        if (Settings.Wheel.Modifier == modifier)
            return true;
        if (modifier == Settings.Trigger)
        {
            Notice?.Invoke("휠 증폭 버튼은 트리거와 같은 버튼을 쓸 수 없습니다.");
            StateChanged?.Invoke(); // let the UI snap back
            return false;
        }
        Update(Settings with { Wheel = Settings.Wheel with { Modifier = modifier } });
        return true;
    }

    public void SetWheelMultiplier(int multiplier)
    {
        if (Settings.Wheel.Multiplier != multiplier)
            Update(Settings with { Wheel = Settings.Wheel with { Multiplier = multiplier } });
    }

    public void SetHoldTimeout(int milliseconds)
    {
        if (Settings.HoldTimeoutMs != milliseconds)
            Update(Settings with { HoldTimeoutMs = milliseconds });
    }

    public void SetDisableInFullscreen(bool disable)
    {
        if (Settings.DisableInFullscreen != disable)
            Update(Settings with { DisableInFullscreen = disable });
    }

    public void SetExcludedApps(IEnumerable<string> processNames)
        => Update(Settings with { ExcludedApps = [.. processNames] });

    /// <summary>Binds (or rebinds) a validated stroke. Returns the action it replaced, if any.</summary>
    public IGestureAction? Bind(string stroke, IGestureAction action)
    {
        var previous = Map.Get(stroke);
        Map.Bind(stroke, action);
        ScheduleSave();
        BindingsChanged?.Invoke();
        return previous;
    }

    public bool Unbind(string stroke)
    {
        if (!Map.Unbind(stroke))
            return false;
        ScheduleSave();
        BindingsChanged?.Invoke();
        return true;
    }

    public async Task RefreshAutoStartAsync()
    {
        IsAutoStartEnabled = await Task.Run(AutoStartManager.IsEnabled);
        StateChanged?.Invoke();
    }

    public async Task SetAutoStartAsync(bool enabled)
    {
        if (IsAutoStartBusy)
            return;
        IsAutoStartBusy = true;
        StateChanged?.Invoke();
        try
        {
            var result = await Task.Run(() => AutoStartManager.Set(enabled));
            IsAutoStartEnabled = await Task.Run(AutoStartManager.IsEnabled);
            if (!result.Success && result.Message is not null)
                Notice?.Invoke(result.Message);
        }
        finally
        {
            IsAutoStartBusy = false;
            StateChanged?.Invoke();
        }
    }

    public void FlushSave()
    {
        _saveTimer.Stop();
        if (!_dirty)
            return;
        try
        {
            _store.Save(Map, Settings);
            _dirty = false;
        }
        catch (Exception ex)
        {
            // Stay dirty so the next change (or exit) retries.
            Logger.Error("Saving settings failed", ex);
            Notice?.Invoke($"설정을 저장하지 못했습니다: {ex.Message}");
        }
    }

    public static string ButtonName(TriggerButton button) => button switch
    {
        TriggerButton.Right => "오른쪽 버튼",
        TriggerButton.Middle => "가운데 버튼",
        TriggerButton.XButton1 => "X1 버튼",
        TriggerButton.XButton2 => "X2 버튼",
        _ => button.ToString(),
    };

    private void Update(AppSettings next)
    {
        Settings = next.Normalized();
        ApplyRuntime();
        ScheduleSave();
        StateChanged?.Invoke();
    }

    private void ApplyRuntime()
    {
        var s = Settings;
        _recognizer.Trigger = s.Trigger;
        _recognizer.HoldTimeoutMs = s.HoldTimeoutMs;
        _recognizer.IsEnabled = !IsPaused;
        _wheel.HoldTimeoutMs = s.HoldTimeoutMs;
        _wheel.Apply(s.Wheel with { Enabled = s.IsWheelEffective && !IsPaused });
        _probe.Configure(s.ExcludedApps, s.DisableInFullscreen);
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
