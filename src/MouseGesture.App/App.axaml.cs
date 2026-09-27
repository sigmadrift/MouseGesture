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
using MouseGesture.Core.Input;
using MouseGesture.Core.Persistence;
using MouseGesture.Core.Recognition;
using R3;

namespace MouseGesture.App;

public partial class App : Application
{
    private SerialWorkQueue? _inputQueue;
    private LowLevelMouseHook? _hook;
    private GestureRecognizer? _recognizer;
    private WheelAmplifier? _wheelAmplifier;
    private GestureDispatcher? _dispatcher;
    private IDisposable? _executedLogSub;
    private AppController? _controller;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;

    private NativeMenuItem? _pauseItem;
    private NativeMenuItem? _autostartItem;

    private ManualResetEvent? _signalStop;
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
            desktop.Exit += (_, _) =>
            {
                DisposeServices();
                EnsureProcessExits();
            };

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Logger.Error("Dispatcher.UnhandledException", e.Exception);
                e.Handled = true;
            };

            try
            {
                InitializeServices();
            }
            catch (Exception ex)
            {
                Logger.Error("Initialization failed", ex);
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeServices()
    {
        var registry = new ActionRegistry();
        var store = new BindingStore(registry);
        var loaded = store.Load();
        if (loaded.CorruptBackupPath is not null || loaded.SkippedEntries > 0)
            Logger.Warn($"Config load: backup={loaded.CorruptBackupPath ?? "-"}, skipped entries={loaded.SkippedEntries}");

        _inputQueue = new SerialWorkQueue("MouseGesture.Input");
        _inputQueue.Faulted += ex => Logger.Error("Synthetic input failed", ex);

        _hook = new LowLevelMouseHook();
        _hook.Diagnostic += Logger.Warn;

        var probe = new ScreenProbe();
        var synthesizer = Win32InputSynthesizer.Instance;
        _recognizer = new GestureRecognizer(_hook, _inputQueue, synthesizer)
        {
            EarlyRecognize = loaded.Map.CanFireEarly,
            ShouldBypass = probe.ShouldBypass,
            DpiScale = ScreenProbe.GetDpiScale,
        };
        _wheelAmplifier = new WheelAmplifier(_hook, _inputQueue, synthesizer)
        {
            ShouldBypass = probe.ShouldBypass,
        };
        _dispatcher = new GestureDispatcher(_recognizer, loaded.Map, _inputQueue);
        _executedLogSub = _dispatcher.Executed.Subscribe(static e =>
        {
            if (e.Error is not null)
                Logger.Error($"Action '{e.Action?.Id}' for gesture '{e.Gesture.Stroke}' failed", e.Error);
        });

        _controller = new AppController(loaded.Map, loaded.Settings, registry, store, _recognizer, _wheelAmplifier, probe);
        _controller.StateChanged += UpdateTrayState;
        _controller.Notice += OnControllerNotice;

        var s = _controller.Settings;
        Logger.Info($"Trigger: {s.Trigger}; Wheel: {s.Wheel.Enabled} {s.Wheel.Modifier} x{s.Wheel.Multiplier}; " +
                    $"Hold: {s.HoldTimeoutMs}ms; Fullscreen off: {s.DisableInFullscreen}; Excluded: {s.ExcludedApps.Length}");

        try
        {
            _hook.Start();
            Logger.Info("Mouse hook installed.");
        }
        catch (Exception ex)
        {
            Logger.Error("SetWindowsHookEx failed", ex);
            Dispatcher.UIThread.Post(() => ShowMessage(
                "마우스 후크를 설치하지 못했습니다.", ex.Message, "다른 후킹 프로그램을 종료한 뒤 다시 실행해 보세요."));
        }

        _trayIcon = BuildTrayIcon();
        TrayIcon.SetIcons(this, [_trayIcon]);

        _ = _controller.RefreshAutoStartAsync();
        _ = Task.Run(AutoStartManager.UpgradeIfNeeded);

        StartSecondInstanceListener();

        if (loaded.CorruptBackupPath is not null)
        {
            Dispatcher.UIThread.Post(() => ShowMessage(
                "설정 파일을 읽지 못해 기본 설정으로 시작합니다.",
                $"원본 파일은 다음 위치에 백업했습니다:\n{loaded.CorruptBackupPath}"));
        }

        if (SessionUserCheck.GetMismatchedSessionUser() is { } sessionUser)
        {
            Logger.Warn($"Running as {Environment.UserDomainName}\\{Environment.UserName} but session user is {sessionUser}.");
            Dispatcher.UIThread.Post(() => ShowMessage(
                "다른 관리자 계정으로 실행 중입니다.",
                $"현재 로그인한 사용자({sessionUser})가 아니라 {Environment.UserName} 계정으로 실행되고 있습니다. " +
                "설정·로그·자동 실행이 모두 그 계정에 저장되며, 자동 실행도 그 계정 로그인 시에만 동작합니다.",
                "관리자 권한이 있는 본인 계정으로 실행하는 것을 권장합니다."));
        }
    }

    private TrayIcon BuildTrayIcon()
    {
        var menu = new NativeMenu();

        var settings = new NativeMenuItem("설정 열기");
        settings.Click += (_, _) => ShowSettings();
        menu.Add(settings);

        menu.Add(new NativeMenuItemSeparator());

        _pauseItem = new NativeMenuItem("일시정지") { ToggleType = MenuItemToggleType.CheckBox };
        _pauseItem.Click += (_, _) => _controller?.SetPaused(!_controller.IsPaused);
        menu.Add(_pauseItem);

        _autostartItem = new NativeMenuItem("Windows 시작 시 자동 실행") { ToggleType = MenuItemToggleType.CheckBox };
        _autostartItem.Click += (_, _) =>
        {
            if (_controller is not null)
                _ = _controller.SetAutoStartAsync(!_controller.IsAutoStartEnabled);
        };
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

        UpdateTrayState();
        return icon;
    }

    private void UpdateTrayState()
    {
        if (_controller is null)
            return;
        if (_pauseItem is not null)
            _pauseItem.IsChecked = _controller.IsPaused;
        if (_autostartItem is not null)
        {
            _autostartItem.IsChecked = _controller.IsAutoStartEnabled;
            _autostartItem.IsEnabled = !_controller.IsAutoStartBusy;
        }
        if (_trayIcon is not null)
            _trayIcon.ToolTipText = _controller.IsPaused ? "MouseGesture (일시정지)" : "MouseGesture";
    }

    private void OnControllerNotice(string message)
    {
        // The settings window shows notices in its status bar; otherwise (e.g. an action
        // from the tray menu) surface it in a small window so it isn't lost.
        if (_settingsWindow is not { IsVisible: true })
            ShowMessage("MouseGesture", message);
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

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = new SettingsViewModel(_controller!, _dispatcher!, _recognizer!);
        _settingsWindow = new SettingsWindow
        {
            DataContext = vm,
            Icon = LoadAppIcon(),
        };
        _settingsWindow.Closed += (_, _) =>
        {
            vm.Dispose();
            _settingsWindow = null;
            _controller?.FlushSave();
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

    private static void ShowMessage(string title, string message, string? hint = null)
    {
        var win = new Window
        {
            Title = "MouseGesture",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Icon = LoadAppIcon(),
        };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        if (message != title)
        {
            panel.Children.Add(new SelectableTextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Opacity = 0.8,
            });
        }
        if (hint is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = hint,
                Opacity = 0.7,
                FontSize = 12,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        var btn = new Button { Content = "확인", Padding = new Thickness(20, 4), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        btn.Click += (_, _) => win.Close();
        panel.Children.Add(btn);
        win.Content = panel;
        win.Show();
    }

    private void StartSecondInstanceListener()
    {
        var showSettings = Program.ShowSettingsEvent;
        var exit = Program.ExitEvent;
        if (showSettings is null && exit is null)
            return;

        // The stop handle is disposed by DisposeServices after joining, never by the thread.
        var stop = _signalStop = new ManualResetEvent(false);
        var handles = new List<WaitHandle> { stop };
        if (showSettings is not null)
            handles.Add(showSettings);
        if (exit is not null)
            handles.Add(exit);
        var waitSet = handles.ToArray();

        _signalThread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    var signaled = waitSet[WaitHandle.WaitAny(waitSet)];
                    if (signaled == stop)
                        return;
                    if (signaled == showSettings)
                    {
                        Dispatcher.UIThread.Post(ShowSettings);
                        continue;
                    }
                    Logger.Info("Exit requested by another process.");
                    Dispatcher.UIThread.Post(Shutdown);
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Instance signal listener crashed", ex);
            }
        })
        {
            IsBackground = true,
            Name = "MouseGesture.InstanceSignals",
        };
        _signalThread.Start();
    }

    private void Shutdown()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.Shutdown();
    }

    /// <summary>
    /// When Windows ends the session — logoff, shutdown, or Restart Manager closing us for an
    /// installer upgrade — Avalonia raises Exit but keeps its message loop running, expecting
    /// the OS to kill the process. Restart Manager instead waits ~30 s before terminating it
    /// (observed during an upgrade). On a normal shutdown Main returns within milliseconds and
    /// this background thread dies with the process, so it only fires in that case.
    /// </summary>
    private static void EnsureProcessExits()
    {
        new Thread(() =>
        {
            Thread.Sleep(3000);
            Logger.Warn("Process still alive 3s after shutdown (session end?); exiting.");
            Logger.Info("=== MouseGesture exited ===");
            Environment.Exit(0);
        })
        {
            IsBackground = true,
            Name = "MouseGesture.ExitWatchdog",
        }.Start();
    }

    private void DisposeServices()
    {
        Logger.Info("Shutting down…");
        _controller?.FlushSave();

        _signalStop?.Set();
        if (_signalThread?.Join(500) ?? true)
            _signalStop?.Dispose();
        _signalStop = null;

        _trayIcon?.Dispose();
        _trayIcon = null;
        _executedLogSub?.Dispose();
        _dispatcher?.Dispose();
        _wheelAmplifier?.Dispose();
        _recognizer?.Dispose();
        _hook?.Dispose();
        _inputQueue?.Dispose();
        _aboutWindow?.Close();
        _settingsWindow?.Close();
    }
}
