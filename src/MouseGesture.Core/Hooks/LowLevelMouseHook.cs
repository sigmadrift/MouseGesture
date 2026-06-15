using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MouseGesture.Core.Native;
using R3;

namespace MouseGesture.Core.Hooks;

/// <summary>
/// Installs a global low-level mouse hook on a dedicated background thread
/// with its own message pump. Single-instance per process.
/// </summary>
public sealed class LowLevelMouseHook : IDisposable
{
    private static LowLevelMouseHook? s_instance;

    private readonly Subject<MouseHookEventArgs> _events = new();
    private readonly MouseHookEventArgs _argsBuffer = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hookHandle;
    private volatile bool _running;
    private Exception? _startError;

    public Observable<MouseHookEventArgs> Events => _events;

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

    private unsafe void HookThread(ManualResetEventSlim ready)
    {
        try
        {
            _threadId = Win32.GetCurrentThreadId();
            var hModule = Win32.GetModuleHandle(null);
            delegate* unmanaged[Stdcall]<int, nuint, nint, nint> proc = &HookProc;
            _hookHandle = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, (IntPtr)proc, hModule, 0);

            if (_hookHandle == IntPtr.Zero)
            {
                _startError = new InvalidOperationException(
                    $"SetWindowsHookEx failed with error {Marshal.GetLastWin32Error()}.");
                ready.Set();
                return;
            }
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
            Win32.TranslateMessage(in msg);
            Win32.DispatchMessage(in msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
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

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe nint HookProc(int nCode, nuint wParam, nint lParam)
    {
        var instance = s_instance;
        if (instance is null || nCode < 0)
            return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

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
            {
                var delta = (short)((data.mouseData >> 16) & 0xFFFF);
                ev = new MouseHookEvent(MouseEventType.Wheel, x, y, delta, 0, time);
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
