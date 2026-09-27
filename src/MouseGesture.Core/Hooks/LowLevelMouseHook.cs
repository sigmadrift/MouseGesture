using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MouseGesture.Core.Native;
using R3;

namespace MouseGesture.Core.Hooks;

/// <summary>
/// Installs a global low-level mouse hook on a dedicated background thread
/// with its own message pump. Single-instance per process.
///
/// Windows silently removes a low-level hook whose callback exceeds
/// LowLevelHooksTimeout (e.g. during a long GC pause). A watchdog on the hook
/// thread notices "the cursor moved and real input happened, but our hook saw
/// nothing" and reinstalls the hook.
/// </summary>
public sealed class LowLevelMouseHook : IMouseEventSource, IDisposable
{
    private const uint WatchdogIntervalMs = 3000;

    private static LowLevelMouseHook? s_instance;

    private readonly Subject<MouseHookEventArgs> _events = new();
    private readonly MouseHookEventArgs _argsBuffer = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hookHandle;
    private volatile bool _running;
    private Exception? _startError;

    // Watchdog state; hook thread only (the hook callback runs on the same thread).
    private int _callbacksSinceCheck;
    private Win32.POINT _lastCursor;
    private uint _lastInputTick;

    public Observable<MouseHookEventArgs> Events => _events;

    /// <summary>Raised (on the hook thread) with diagnostic messages such as hook reinstalls.</summary>
    public event Action<string>? Diagnostic;

    public void Start()
    {
        if (_running)
            return;

        if (Interlocked.CompareExchange(ref s_instance, this, null) is not null)
            throw new InvalidOperationException("Only one LowLevelMouseHook can run at a time.");

        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => HookThread(ready))
        {
            IsBackground = true,
            Name = "MouseGesture.Hook",
            // Keep the callback responsive under load so Windows doesn't drop the hook.
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        ready.Wait();

        if (_startError is not null)
        {
            Interlocked.Exchange(ref s_instance, null);
            throw _startError;
        }
        _running = true;
    }

    private void HookThread(ManualResetEventSlim ready)
    {
        nuint timerId = 0;
        try
        {
            _threadId = Win32.GetCurrentThreadId();
            if (!Install())
            {
                _startError = new InvalidOperationException(
                    $"SetWindowsHookEx failed with error {Marshal.GetLastWin32Error()}.");
                ready.Set();
                return;
            }
            timerId = Win32.SetTimer(IntPtr.Zero, 0, WatchdogIntervalMs, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _startError = ex;
            ready.Set();
            return;
        }

        ready.Set();

        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == Win32.WM_TIMER && msg.hwnd == IntPtr.Zero)
            {
                CheckHookHealth();
                continue;
            }
            Win32.TranslateMessage(in msg);
            Win32.DispatchMessage(in msg);
        }

        if (timerId != 0)
            Win32.KillTimer(IntPtr.Zero, timerId);
        if (_hookHandle != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private unsafe bool Install()
    {
        var hModule = Win32.GetModuleHandle(null);
        delegate* unmanaged[Stdcall]<int, nuint, nint, nint> proc = &HookProc;
        _hookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, (IntPtr)proc, hModule, 0);
        ResetWatchdogBaseline();
        return _hookHandle != IntPtr.Zero;
    }

    // Start each watch period from the current state; otherwise the first check compares
    // against zeroed values and "detects" movement the hook never had a chance to see.
    private void ResetWatchdogBaseline()
    {
        Win32.GetCursorPos(out _lastCursor);
        var lii = new Win32.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Win32.LASTINPUTINFO>() };
        Win32.GetLastInputInfo(ref lii);
        _lastInputTick = lii.dwTime;
        _callbacksSinceCheck = 0;
    }

    private void CheckHookHealth()
    {
        try
        {
            Win32.GetCursorPos(out var pos);
            var lii = new Win32.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Win32.LASTINPUTINFO>() };
            Win32.GetLastInputInfo(ref lii);

            // SetCursorPos moves the cursor without counting as input, and keyboard input
            // doesn't move the cursor, so requiring both avoids most false positives.
            var cursorMoved = pos.X != _lastCursor.X || pos.Y != _lastCursor.Y;
            var hadInput = lii.dwTime != _lastInputTick;
            var hookSilent = _callbacksSinceCheck == 0;

            _lastCursor = pos;
            _lastInputTick = lii.dwTime;
            _callbacksSinceCheck = 0;

            if (!(cursorMoved && hadInput && hookSilent))
                return;

            if (_hookHandle != IntPtr.Zero)
                Win32.UnhookWindowsHookEx(_hookHandle);
            Diagnostic?.Invoke(Install()
                ? "Mouse hook stopped receiving events (likely removed by Windows); reinstalled."
                : $"Mouse hook reinstall failed with error {Marshal.GetLastWin32Error()}.");
        }
        catch
        {
            // Watchdog must never kill the hook thread.
        }
    }

    public void Dispose()
    {
        if (!_running)
            return;
        _running = false;

        Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, 0, 0);
        _thread?.Join(1000);
        _events.Dispose();
        Interlocked.Exchange(ref s_instance, null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint HookProc(int nCode, nuint wParam, nint lParam)
    {
        var instance = s_instance;
        if (instance is null || nCode < 0)
            return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        instance._callbacksSinceCheck++;

        bool suppress = false;
        try
        {
            var data = *(Win32.MSLLHOOKSTRUCT*)lParam;
            if (data.dwExtraInfo == Win32.INPUT_MARKER)
                return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            if (TryParse((int)wParam, in data, out var ev))
            {
                var args = instance._argsBuffer;
                args.Event = ev;
                args.Suppress = false;
                instance._events.OnNext(args);
                suppress = args.Suppress;
            }
        }
        catch
        {
            // Never let exceptions cross the native boundary.
        }

        return suppress ? 1 : Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParse(int msg, in Win32.MSLLHOOKSTRUCT data, out MouseHookEvent ev)
    {
        var x = data.pt.X;
        var y = data.pt.Y;
        var time = data.time;

        switch (msg)
        {
            case Win32.WM_MOUSEMOVE:
                ev = new MouseHookEvent(MouseEventType.Move, x, y, 0, 0, time);
                return true;
            case Win32.WM_LBUTTONDOWN:
                ev = new MouseHookEvent(MouseEventType.LeftDown, x, y, 0, 0, time);
                return true;
            case Win32.WM_LBUTTONUP:
                ev = new MouseHookEvent(MouseEventType.LeftUp, x, y, 0, 0, time);
                return true;
            case Win32.WM_RBUTTONDOWN:
                ev = new MouseHookEvent(MouseEventType.RightDown, x, y, 0, 0, time);
                return true;
            case Win32.WM_RBUTTONUP:
                ev = new MouseHookEvent(MouseEventType.RightUp, x, y, 0, 0, time);
                return true;
            case Win32.WM_MBUTTONDOWN:
                ev = new MouseHookEvent(MouseEventType.MiddleDown, x, y, 0, 0, time);
                return true;
            case Win32.WM_MBUTTONUP:
                ev = new MouseHookEvent(MouseEventType.MiddleUp, x, y, 0, 0, time);
                return true;
            case Win32.WM_MOUSEWHEEL:
            case Win32.WM_MOUSEHWHEEL:
            {
                var delta = (short)((data.mouseData >> 16) & 0xFFFF);
                var type = msg == Win32.WM_MOUSEWHEEL ? MouseEventType.Wheel : MouseEventType.HWheel;
                ev = new MouseHookEvent(type, x, y, delta, 0, time);
                return true;
            }
            case Win32.WM_XBUTTONDOWN:
            {
                var btn = (int)((data.mouseData >> 16) & 0xFFFF);
                ev = new MouseHookEvent(MouseEventType.XButtonDown, x, y, 0, btn, time);
                return true;
            }
            case Win32.WM_XBUTTONUP:
            {
                var btn = (int)((data.mouseData >> 16) & 0xFFFF);
                ev = new MouseHookEvent(MouseEventType.XButtonUp, x, y, 0, btn, time);
                return true;
            }
            default:
                ev = default;
                return false;
        }
    }
}
