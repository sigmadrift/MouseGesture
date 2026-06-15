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

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private const int RecentLimit = 30;

    private readonly GestureMap _map;
    private readonly GestureRecognizer _recognizer;
    private readonly WheelAmplifier _wheelAmplifier;
    private readonly ActionRegistry _registry;
    private readonly BindingStore _store;
    private readonly IDisposable _executedSub;
    private readonly IDisposable _strokeSub;
    private bool _suppressTriggerSave;
    private bool _suppressPause;
    private bool _suppressAutoStart;
    private bool _suppressWheelSave;

    public ObservableCollection<BindingRow> Bindings { get; } = new();
    public ObservableCollection<string> RecentLog { get; } = new();
    public IReadOnlyList<IGestureAction> AvailableActions { get; }
    public IReadOnlyList<TriggerOption> TriggerOptions => TriggerOption.All;
    public IReadOnlyList<TriggerOption> WheelModifierOptions => TriggerOption.WheelModifiers;

    public decimal WheelMultiplierMin => WheelSettings.MinMultiplier;
    public decimal WheelMultiplierMax => WheelSettings.MaxMultiplier;

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
    private bool _wheelEnabled;

    [ObservableProperty]
    private TriggerOption _selectedWheelModifier = TriggerOption.WheelModifiers[0];

    [ObservableProperty]
    private decimal _wheelMultiplier = WheelSettings.Default.Multiplier;

    public SettingsViewModel(
        GestureMap map,
        GestureDispatcher dispatcher,
        GestureRecognizer recognizer,
        WheelAmplifier wheelAmplifier,
        ActionRegistry registry,
        BindingStore store)
    {
        _map = map;
        _recognizer = recognizer;
        _wheelAmplifier = wheelAmplifier;
        _registry = registry;
        _store = store;
        AvailableActions = registry.AllActions;
        _selectedAction = AvailableActions.FirstOrDefault();
        RefreshBindings();

        // Initialize from current state without triggering save side-effects.
        _suppressTriggerSave = true;
        _selectedTrigger = TriggerOption.ForValue(recognizer.Trigger);
        _suppressTriggerSave = false;

        _suppressPause = true;
        _isPaused = !recognizer.IsEnabled;
        _suppressPause = false;

        _suppressAutoStart = true;
        _isAutoStartEnabled = AutoStartManager.IsEnabled();
        _suppressAutoStart = false;

        _suppressWheelSave = true;
        _wheelEnabled = wheelAmplifier.IsEnabled;
        _selectedWheelModifier = WheelModifierOptions.FirstOrDefault(o => o.Value == wheelAmplifier.Modifier)
            ?? WheelModifierOptions[0];
        _wheelMultiplier = wheelAmplifier.Multiplier;
        _suppressWheelSave = false;

        _executedSub = dispatcher.Executed.Subscribe(this, static (entry, self) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var arrows = DirectionExtensions.StrokeToArrows(entry.Gesture.Stroke);
                var name = entry.Action?.Name ?? "(매핑 없음)";
                self.RecentLog.Insert(0, $"{DateTime.Now:HH:mm:ss}  {arrows}  →  {name}");
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

    partial void OnLiveStrokeChanged(string value)
        => LiveStrokeArrows = DirectionExtensions.StrokeToArrows(value);

    partial void OnNewStrokeChanged(string value)
        => NewStrokeArrows = DirectionExtensions.StrokeToArrows(value);

    partial void OnSelectedTriggerChanged(TriggerOption value)
    {
        if (_suppressTriggerSave)
            return;
        if (value.Value == SelectedWheelModifier.Value)
        {
            // Trigger and wheel modifier must differ; revert to the current trigger.
            StatusMessage = "트리거와 휠 증폭 버튼은 같은 버튼을 쓸 수 없습니다.";
            _suppressTriggerSave = true;
            SelectedTrigger = TriggerOption.ForValue(_recognizer.Trigger);
            _suppressTriggerSave = false;
            return;
        }
        _recognizer.Trigger = value.Value;
        _store.Save(_map, value.Value);
        StatusMessage = $"트리거 버튼: {value.Display}";
    }

    partial void OnIsPausedChanged(bool value)
    {
        if (_suppressPause)
            return;
        _recognizer.IsEnabled = !value;
        _wheelAmplifier.IsEnabled = !value && WheelEnabled;
    }

    partial void OnWheelEnabledChanged(bool value) => SaveWheel();

    partial void OnSelectedWheelModifierChanged(TriggerOption value)
    {
        if (_suppressWheelSave)
            return;
        if (value.Value == SelectedTrigger.Value)
        {
            // Wheel modifier and trigger must differ; revert to the current modifier.
            StatusMessage = "휠 증폭 버튼과 트리거는 같은 버튼을 쓸 수 없습니다.";
            _suppressWheelSave = true;
            SelectedWheelModifier = TriggerOption.ForValue(_wheelAmplifier.Modifier);
            _suppressWheelSave = false;
            return;
        }
        SaveWheel();
    }

    partial void OnWheelMultiplierChanged(decimal value)
    {
        var clamped = Math.Clamp(value, WheelMultiplierMin, WheelMultiplierMax);
        if (clamped != value)
        {
            WheelMultiplier = clamped; // re-enters; the clamped pass performs the save
            return;
        }
        SaveWheel();
    }

    private void SaveWheel()
    {
        if (_suppressWheelSave)
            return;
        var settings = new WheelSettings(WheelEnabled, SelectedWheelModifier.Value, (int)WheelMultiplier);
        _wheelAmplifier.Apply(settings);
        _store.Save(_map, _recognizer.Trigger, settings);
        StatusMessage = WheelEnabled
            ? $"휠 증폭: {SelectedWheelModifier.Display} + 휠 → {(int)WheelMultiplier}배"
            : "휠 증폭 사용 안 함";
    }

    partial void OnIsAutoStartEnabledChanged(bool value)
    {
        if (_suppressAutoStart)
            return;
        AutoStartManager.Set(value);
        // Re-read in case the registry write was rejected.
        _suppressAutoStart = true;
        IsAutoStartEnabled = AutoStartManager.IsEnabled();
        _suppressAutoStart = false;
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
        if (NewStroke.Length >= 8)
            return;
        // Avoid recording the same direction twice in a row (matches the recognizer behavior).
        if (NewStroke.Length > 0 && NewStroke[^1] == dir)
            return;
        NewStroke += dir;
    }

    [RelayCommand]
    private void AddBinding()
    {
        var stroke = NewStroke;
        if (string.IsNullOrEmpty(stroke))
        {
            StatusMessage = "방향 버튼으로 제스처를 입력해 주세요.";
            return;
        }
        if (SelectedAction is null)
        {
            StatusMessage = "동작을 선택해 주세요.";
            return;
        }

        _map.Bind(stroke, SelectedAction);
        _store.Save(_map, _recognizer.Trigger);
        StatusMessage = $"매핑 추가됨: {DirectionExtensions.StrokeToArrows(stroke)} → {SelectedAction.Name}";
        NewStroke = string.Empty;
        RefreshBindings();
    }

    private void RemoveBinding(string stroke)
    {
        if (_map.Unbind(stroke))
        {
            _store.Save(_map, _recognizer.Trigger);
            StatusMessage = $"매핑 삭제됨: {DirectionExtensions.StrokeToArrows(stroke)}";
            RefreshBindings();
        }
    }

    private void RefreshBindings()
    {
        Bindings.Clear();
        foreach (var (stroke, action) in _map.Bindings.OrderBy(p => p.Key.Length).ThenBy(p => p.Key, StringComparer.Ordinal))
            Bindings.Add(new BindingRow(stroke, action.Id, action.Name, RemoveBinding));
    }

    public void Dispose()
    {
        _executedSub.Dispose();
        _strokeSub.Dispose();
    }
}
