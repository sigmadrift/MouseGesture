using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using MouseGesture.App.Services;
using MouseGesture.App.ViewModels;
using MouseGesture.App.Views;
using MouseGesture.Core.Actions;
using MouseGesture.Core.Hooks;
using MouseGesture.Core.Persistence;
using MouseGesture.Core.Recognition;

namespace MouseGesture.App;

public partial class App : Application
{
    private LowLevelMouseHook? _hook;
    private GestureRecognizer? _recognizer;
    private WheelAmplifier? _wheelAmplifier;
    private GestureDispatcher? _dispatcher;
    private GestureMap? _map;
    private ActionRegistry? _actionRegistry;
    private BindingStore? _bindingStore;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;

    private NativeMenuItem? _pauseItem;
    private NativeMenuItem? _autostartItem;

    private EventWaitHandle? _showSettingsEvent;
    private CancellationTokenSource? _shutdownCts;
    private Thread? _signalThread;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => DisposeServices();

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Logger.Error("Dispatcher.UnhandledException", e.Exception);
                e.Handled = true;
            };

            try
            {
                _actionRegistry = new ActionRegistry();
                _bindingStore = new BindingStore(_actionRegistry);
                _map = _bindingStore.LoadOrDefault();
                var trigger = _bindingStore.LoadTrigger();
                _hook = new LowLevelMouseHook();
                _recognizer = new GestureRecognizer(_hook)
                {
                    Trigger = trigger,
                    EarlyRecognize = _map.IsBound,
                };
                var wheelSettings = _bindingStore.LoadWheelSettings();
                if (wheelSettings.Enabled && wheelSettings.Modifier == trigger)
                {
                    // Invalid config (e.g. hand-edited JSON): the same button can't be
                    // both the gesture trigger and the wheel modifier. Disable wheel
                    // amplification rather than have both features fight over the button.
                    Logger.Warn($"Wheel modifier ({wheelSettings.Modifier}) equals trigger; disabling wheel amplification.");
                    wheelSettings = wheelSettings with { Enabled = false };
                }
                _wheelAmplifier = new WheelAmplifier(_hook);
                _wheelAmplifier.Apply(wheelSettings);
                _dispatcher = new GestureDispatcher(_recognizer, _map);
                Logger.Info($"Trigger: {trigger}; Wheel: {wheelSettings.Enabled} {wheelSettings.Modifier} x{wheelSettings.Multiplier}");

                try
                {
                    _hook.Start();
                    Logger.Info("Mouse hook installed.");
                }
                catch (Exception ex)
                {
                    Logger.Error("SetWindowsHookEx failed", ex);
                    Dispatcher.UIThread.Post(() => ShowHookFailureDialog(ex.Message));
                }

                _trayIcon = BuildTrayIcon();
                TrayIcon.SetIcons(this, [_trayIcon]);

                StartSecondInstanceListener();
            }
            catch (Exception ex)
            {
                Logger.Error("Initialization failed", ex);
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon BuildTrayIcon()
    {
        var menu = new NativeMenu();

        var settings = new NativeMenuItem("설정 열기");
        settings.Click += (_, _) => ShowSettings();
        menu.Add(settings);

        menu.Add(new NativeMenuItemSeparator());

        _pauseItem = new NativeMenuItem("일시정지")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = false,
        };
        _pauseItem.Click += (_, _) => TogglePaused();
        menu.Add(_pauseItem);

        _autostartItem = new NativeMenuItem("Windows 시작 시 자동 실행")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = AutoStartManager.IsEnabled(),
        };
        _autostartItem.Click += (_, _) => ToggleAutoStart();
        menu.Add(_autostartItem);

        menu.Add(new NativeMenuItemSeparator());

        var about = new NativeMenuItem("정보");
        about.Click += (_, _) => ShowAbout();
        menu.Add(about);

        var exit = new NativeMenuItem("종료");
        exit.Click += (_, _) => Shutdown();
        menu.Add(exit);

        var icon = new TrayIcon
        {
            Icon = LoadAppIcon(),
            ToolTipText = "MouseGesture",
            Menu = menu,
        };
        icon.Clicked += (_, _) => ShowSettings();
        return icon;
    }

    private static WindowIcon LoadAppIcon()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://MouseGesture.App/Assets/app.ico"));
            return new WindowIcon(s);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Falling back to generated icon: {ex.Message}");
            return GenerateFallbackIcon();
        }
    }

    private static WindowIcon GenerateFallbackIcon()
    {
        var size = new PixelSize(32, 32);
        var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            ctx.FillRectangle(
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x29, 0x82, 0xFF)),
                new Rect(0, 0, 32, 32));
            var pen = new Avalonia.Media.Pen(Avalonia.Media.Brushes.White, 2.5);
            ctx.DrawLine(pen, new Point(8, 16), new Point(24, 16));
            ctx.DrawLine(pen, new Point(20, 12), new Point(24, 16));
            ctx.DrawLine(pen, new Point(20, 20), new Point(24, 16));
        }
        using var ms = new MemoryStream();
        bmp.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    private void TogglePaused()
    {
        if (_recognizer is null || _pauseItem is null)
            return;
        var active = !_recognizer.IsEnabled;
        _recognizer.IsEnabled = active;
        // Pause also stops wheel amplification so everything passes through; on resume
        // restore the wheel feature to its configured on/off state.
        if (_wheelAmplifier is not null)
            _wheelAmplifier.IsEnabled = active && (_bindingStore?.LoadWheelSettings().Enabled ?? false);
        _pauseItem.IsChecked = !active;
        if (_trayIcon is not null)
            _trayIcon.ToolTipText = active ? "MouseGesture" : "MouseGesture (일시정지)";
        Logger.Info($"Pause: {!active}");
    }

    private void ToggleAutoStart()
    {
        if (_autostartItem is null)
            return;
        var nextState = !AutoStartManager.IsEnabled();
        AutoStartManager.Set(nextState);
        _autostartItem.IsChecked = AutoStartManager.IsEnabled();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(_map!, _dispatcher!, _recognizer!, _wheelAmplifier!, _actionRegistry!, _bindingStore!);
        _settingsWindow = new SettingsWindow
        {
            DataContext = vm,
            Icon = LoadAppIcon(),
        };
        _settingsWindow.Closed += (_, _) =>
        {
            vm.Dispose();
            _settingsWindow = null;
        };
        _settingsWindow.Show();
    }

    private void ShowAbout()
    {
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }
        _aboutWindow = new AboutWindow { Icon = LoadAppIcon() };
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        if (_settingsWindow is { IsVisible: true })
            _aboutWindow.ShowDialog(_settingsWindow);
        else
            _aboutWindow.Show();
    }

    private static void ShowHookFailureDialog(string message)
    {
        var win = new Window
        {
            Title = "MouseGesture",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "마우스 후크를 설치하지 못했습니다.",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 14,
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.8,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "다른 후킹 프로그램을 종료한 뒤 다시 실행해 보세요.",
            Opacity = 0.7,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
        });
        var btn = new Button { Content = "확인", Padding = new Thickness(20, 4), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        btn.Click += (_, _) => win.Close();
        panel.Children.Add(btn);
        win.Content = panel;
        win.Show();
    }

    private void StartSecondInstanceListener()
    {
        try
        {
            _showSettingsEvent = SingleInstance.CreateShowSettingsEvent();
        }
        catch (Exception ex)
        {
            Logger.Warn($"CreateShowSettingsEvent failed: {ex.Message}");
            return;
        }

        _shutdownCts = new CancellationTokenSource();
        var stopHandle = new ManualResetEvent(false);
        _shutdownCts.Token.Register(() => stopHandle.Set());

        _signalThread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _showSettingsEvent, stopHandle };
            try
            {
                while (true)
                {
                    var idx = WaitHandle.WaitAny(handles);
                    if (idx == 1)
                        break;
                    Dispatcher.UIThread.Post(ShowSettings);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Second-instance listener crashed", ex);
            }
            finally
            {
                stopHandle.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "MouseGesture.SecondInstance",
        };
        _signalThread.Start();
    }

    private void Shutdown()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.Shutdown();
    }

    private void DisposeServices()
    {
        Logger.Info("Shutting down…");
        _shutdownCts?.Cancel();
        _signalThread?.Join(500);
        _showSettingsEvent?.Dispose();
        _showSettingsEvent = null;
        _shutdownCts?.Dispose();
        _shutdownCts = null;

        _trayIcon?.Dispose();
        _trayIcon = null;
        _dispatcher?.Dispose();
        _wheelAmplifier?.Dispose();
        _recognizer?.Dispose();
        _hook?.Dispose();
        _aboutWindow?.Close();
        _settingsWindow?.Close();
    }
}
