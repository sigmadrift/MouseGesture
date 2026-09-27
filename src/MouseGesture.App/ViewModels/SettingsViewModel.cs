using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MouseGesture.App.Services;
using MouseGesture.Core.Actions;
using MouseGesture.Core.Persistence;
using MouseGesture.Core.Recognition;
using R3;

namespace MouseGesture.App.ViewModels;

/// <summary>
/// Thin view over <see cref="AppController"/>: user edits are forwarded to it, and every
/// controller change is mirrored back (so the tray and this window never disagree).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private const int RecentLimit = 30;

    private readonly AppController _controller;
    private readonly IDisposable _executedSub;
    private readonly IDisposable _strokeSub;
    private bool _syncing;
    private string? _syncedExcludedApps;

    public ObservableCollection<BindingRow> Bindings { get; } = new();
    public ObservableCollection<string> RecentLog { get; } = new();
    public IReadOnlyList<IGestureAction> AvailableActions { get; }
    public IReadOnlyList<TriggerOption> TriggerOptions => TriggerOption.All;
    public IReadOnlyList<TriggerOption> WheelModifierOptions => TriggerOption.WheelModifiers;

    public decimal WheelMultiplierMin => WheelSettings.MinMultiplier;
    public decimal WheelMultiplierMax => WheelSettings.MaxMultiplier;
    public decimal HoldTimeoutMin => AppSettings.MinHoldTimeoutMs;
    public decimal HoldTimeoutMax => AppSettings.MaxHoldTimeoutMs;
    public int MaxStrokeLength => StrokeFormat.MaxLength;

    [ObservableProperty]
    private string _liveStroke = string.Empty;

    [ObservableProperty]
    private string _liveStrokeArrows = string.Empty;

    [ObservableProperty]
    private string _newStroke = string.Empty;

    [ObservableProperty]
    private string _newStrokeArrows = string.Empty;

    [ObservableProperty]
    private IGestureAction? _selectedAction;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private TriggerOption _selectedTrigger = TriggerOption.All[0];

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    [ObservableProperty]
    private bool _isAutoStartBusy;

    [ObservableProperty]
    private bool _wheelEnabled;

    [ObservableProperty]
    private bool _isWheelBlockedByTrigger;

    [ObservableProperty]
    private TriggerOption _selectedWheelModifier = TriggerOption.WheelModifiers[0];

    [ObservableProperty]
    private decimal? _wheelMultiplier = WheelSettings.Default.Multiplier;

    [ObservableProperty]
    private decimal? _holdTimeoutMs = AppSettings.Default.HoldTimeoutMs;

    [ObservableProperty]
    private bool _disableInFullscreen;

    [ObservableProperty]
    private string _excludedAppsText = string.Empty;

    public ObservableCollection<RunningApp> RunningApps { get; } = new();

    [ObservableProperty]
    private bool _isRunningAppsOpen;

    [ObservableProperty]
    private RunningApp? _selectedRunningApp;

    public SettingsViewModel(AppController controller, GestureDispatcher dispatcher, GestureRecognizer recognizer)
    {
        _controller = controller;
        AvailableActions = controller.Registry.AllActions;
        _selectedAction = AvailableActions.FirstOrDefault();

        SyncFromController();
        RefreshBindings();

        _controller.StateChanged += OnControllerStateChanged;
        _controller.BindingsChanged += RefreshBindings;
        _controller.Notice += OnNotice;

        _executedSub = dispatcher.Executed.Subscribe(this, static (entry, self) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var arrows = DirectionExtensions.StrokeToArrows(entry.Gesture.Stroke);
                var name = entry.Action?.Name ?? "(매핑 없음)";
                var suffix = entry.Error is null ? "" : $"  ⚠ 실패: {entry.Error.Message}";
                self.RecentLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {arrows}  →  {name}{suffix}");
                while (self.RecentLog.Count > RecentLimit)
                    self.RecentLog.RemoveAt(self.RecentLog.Count - 1);
                self.LiveStroke = string.Empty;
            });
        });

        _strokeSub = recognizer.StrokeAdded.Subscribe(this, static (dir, self) =>
        {
            Dispatcher.UIThread.Post(() => self.LiveStroke += dir.ToChar());
        });
    }

    // Posted rather than applied inline: a change may originate from this VM's own
    // property setter (e.g. a rejected combo selection), and re-setting a bound property
    // from inside its change notification doesn't reliably reach the control.
    private void OnControllerStateChanged() => Dispatcher.UIThread.Post(SyncFromController);

    private void OnNotice(string message) => StatusMessage = message;

    private void SyncFromController()
    {
        _syncing = true;
        try
        {
            var s = _controller.Settings;
            SelectedTrigger = TriggerOption.ForValue(s.Trigger);
            IsPaused = _controller.IsPaused;
            IsAutoStartEnabled = _controller.IsAutoStartEnabled;
            IsAutoStartBusy = _controller.IsAutoStartBusy;
            // The configured state, not the amplifier's live state (which is off while paused).
            WheelEnabled = s.Wheel.Enabled;
            IsWheelBlockedByTrigger = s.Wheel.Enabled && !s.IsWheelEffective;
            SelectedWheelModifier = WheelModifierOptions.FirstOrDefault(o => o.Value == s.Wheel.Modifier)
                ?? WheelModifierOptions[0];
            WheelMultiplier = s.Wheel.Multiplier;
            HoldTimeoutMs = s.HoldTimeoutMs;
            DisableInFullscreen = s.DisableInFullscreen;
            // Only when the applied list changed, so unrelated updates (pause, …) don't wipe
            // edits the user hasn't applied yet.
            var excluded = string.Join(Environment.NewLine, s.ExcludedApps);
            if (excluded != _syncedExcludedApps)
            {
                ExcludedAppsText = excluded;
                _syncedExcludedApps = excluded;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnLiveStrokeChanged(string value)
        => LiveStrokeArrows = DirectionExtensions.StrokeToArrows(value);

    partial void OnNewStrokeChanged(string value)
        => NewStrokeArrows = DirectionExtensions.StrokeToArrows(value);

    partial void OnSelectedTriggerChanged(TriggerOption value)
    {
        if (_syncing || value is null)
            return;
        StatusMessage = $"트리거 버튼: {value.Display}";
        _controller.SetTrigger(value.Value); // may replace the message with a conflict notice
    }

    partial void OnIsPausedChanged(bool value)
    {
        if (!_syncing)
            _controller.SetPaused(value);
    }

    partial void OnIsAutoStartEnabledChanged(bool value)
    {
        if (!_syncing)
            _ = _controller.SetAutoStartAsync(value);
    }

    partial void OnWheelEnabledChanged(bool value)
    {
        if (!_syncing)
            _controller.SetWheelEnabled(value);
    }

    partial void OnSelectedWheelModifierChanged(TriggerOption value)
    {
        if (!_syncing && value is not null)
            _controller.SetWheelModifier(value.Value);
    }

    partial void OnWheelMultiplierChanged(decimal? value)
    {
        if (_syncing)
            return;
        if (value is { } v)
            _controller.SetWheelMultiplier((int)Math.Round(v));
        else
            OnControllerStateChanged(); // cleared box: restore the current value
    }

    partial void OnHoldTimeoutMsChanged(decimal? value)
    {
        if (_syncing)
            return;
        if (value is { } v)
            _controller.SetHoldTimeout((int)Math.Round(v));
        else
            OnControllerStateChanged();
    }

    partial void OnDisableInFullscreenChanged(bool value)
    {
        if (!_syncing)
            _controller.SetDisableInFullscreen(value);
    }

    [RelayCommand]
    private void ApplyExcludedApps()
    {
        _syncedExcludedApps = null; // always show the normalized result
        var names = ExcludedAppsText.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _controller.SetExcludedApps(names);
        var count = _controller.Settings.ExcludedApps.Length;
        StatusMessage = count == 0 ? "제외 앱 없음" : $"제외 앱 {count}개 적용됨";
    }

    // Refresh the list each time the drop-down opens, so it reflects what's running now.
    partial void OnIsRunningAppsOpenChanged(bool value)
    {
        if (!value)
            return;
        var excluded = _controller.Settings.ExcludedApps.ToHashSet(StringComparer.OrdinalIgnoreCase);
        RunningApps.Clear();
        foreach (var app in ScreenProbe.GetRunningApps())
        {
            if (!excluded.Contains(app.ProcessName))
                RunningApps.Add(app);
        }
    }

    partial void OnSelectedRunningAppChanged(RunningApp? value)
    {
        if (value is null)
            return;
        _controller.SetExcludedApps([.. _controller.Settings.ExcludedApps, value.ProcessName]);
        StatusMessage = $"제외 앱 추가됨: {value.ProcessName}";
        // Reset so the same entry can be picked again later; posted because changing the
        // selection from inside its own change notification doesn't reach the control.
        Dispatcher.UIThread.Post(() => SelectedRunningApp = null);
    }

    [RelayCommand]
    private void AppendUp() => AppendDirection('U');

    [RelayCommand]
    private void AppendDown() => AppendDirection('D');

    [RelayCommand]
    private void AppendLeft() => AppendDirection('L');

    [RelayCommand]
    private void AppendRight() => AppendDirection('R');

    [RelayCommand]
    private void ClearStroke()
    {
        NewStroke = string.Empty;
        StatusMessage = null;
    }

    [RelayCommand]
    private void Backspace()
    {
        if (string.IsNullOrEmpty(NewStroke))
            return;
        NewStroke = NewStroke[..^1];
    }

    private void AppendDirection(char dir)
    {
        if (NewStroke.Length >= StrokeFormat.MaxLength)
        {
            StatusMessage = $"제스처는 최대 {StrokeFormat.MaxLength}방향까지 입력할 수 있습니다.";
            return;
        }
        // Avoid recording the same direction twice in a row (matches the recognizer behavior).
        if (NewStroke.Length > 0 && NewStroke[^1] == dir)
            return;
        NewStroke += dir;
    }

    [RelayCommand]
    private void AddBinding()
    {
        if (!StrokeFormat.TryNormalize(NewStroke, out var stroke))
        {
            StatusMessage = "방향 버튼으로 제스처를 입력해 주세요.";
            return;
        }
        if (SelectedAction is not { } action)
        {
            StatusMessage = "동작을 선택해 주세요.";
            return;
        }

        var arrows = DirectionExtensions.StrokeToArrows(stroke);
        var previous = _controller.Bind(stroke, action);
        StatusMessage = previous is not null && previous != action
            ? $"{arrows}: '{previous.Name}' → '{action.Name}'(으)로 바꿨습니다."
            : $"매핑 추가됨: {arrows} → {action.Name}";

        // Explain why a shorter gesture no longer fires mid-drag.
        var waiting = Enumerable.Range(1, stroke.Length - 1)
            .Select(n => stroke[..n])
            .Where(_controller.Map.IsBound)
            .Select(DirectionExtensions.StrokeToArrows)
            .ToList();
        if (waiting.Count > 0)
            StatusMessage += $" ({string.Join(", ", waiting)} 은(는) 이제 버튼을 뗄 때 실행됩니다.)";

        NewStroke = string.Empty;
    }

    private void RemoveBinding(string stroke)
    {
        if (_controller.Unbind(stroke))
            StatusMessage = $"매핑 삭제됨: {DirectionExtensions.StrokeToArrows(stroke)}";
    }

    private void RefreshBindings()
    {
        Bindings.Clear();
        var map = _controller.Map;
        foreach (var (stroke, action) in map.Bindings.OrderBy(p => p.Key.Length).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var description = map.HasLongerBinding(stroke) ? $"{action.Name}  (뗄 때 실행)" : action.Name;
            Bindings.Add(new BindingRow(stroke, action.Id, description, RemoveBinding));
        }
    }

    public void Dispose()
    {
        _controller.StateChanged -= OnControllerStateChanged;
        _controller.BindingsChanged -= RefreshBindings;
        _controller.Notice -= OnNotice;
        _executedSub.Dispose();
        _strokeSub.Dispose();
    }
}
