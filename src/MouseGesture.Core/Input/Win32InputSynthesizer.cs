using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseGesture.Core.Native;
using MouseGesture.Core.Recognition;

namespace MouseGesture.Core.Input;

/// <summary>
/// SendInput-based synthesizer. All injected events carry <see cref="Win32.INPUT_MARKER"/>
/// in dwExtraInfo so our own hook ignores them (preventing re-entrant capture).
/// </summary>
public sealed class Win32InputSynthesizer : IInputSynthesizer
{
    public static Win32InputSynthesizer Instance { get; } = new();

    private Win32InputSynthesizer()
    {
    }

    public void ButtonDown(TriggerButton button)
    {
        if (TryGetFlags(button, out var down, out _, out var data))
            Send([MakeMouse(down, data)]);
    }

    public void ButtonUp(TriggerButton button)
    {
        if (TryGetFlags(button, out _, out var up, out var data))
            Send([MakeMouse(up, data)]);
    }

    public void ButtonClick(TriggerButton button)
    {
        if (TryGetFlags(button, out var down, out var up, out var data))
            Send([MakeMouse(down, data), MakeMouse(up, data)]);
    }

    /// <remarks>
    /// Emitting separate notches (rather than one big delta) makes apps that ignore the
    /// delta magnitude — and only scroll a fixed step per WM_MOUSEWHEEL — still amplify;
    /// apps that respect the magnitude get the same total.
    /// Each notch is its own SendInput call with a 1 ms gap: several wheel events injected
    /// in one batch (or back-to-back) get merged by Windows and some of them are lost —
    /// measured: 3 batched notches consistently arrived as 240 instead of 360.
    /// </remarks>
    public void Wheel(int notchDelta, int count, bool horizontal)
    {
        if (count < 1 || notchDelta == 0)
            return;

        var flag = horizontal ? Win32.MOUSEEVENTF_HWHEEL : Win32.MOUSEEVENTF_WHEEL;
        var input = new[] { MakeMouse(flag, unchecked((uint)notchDelta)) };
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                Thread.Sleep(1);
            Send(input);
        }
    }

    private static void Send(Win32.INPUT[] inputs)
    {
        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput (mouse) was blocked or failed.");
    }

    private static bool TryGetFlags(TriggerButton button, out uint downFlag, out uint upFlag, out uint mouseData)
    {
        mouseData = 0;
        switch (button)
        {
            case TriggerButton.Right:
                downFlag = Win32.MOUSEEVENTF_RIGHTDOWN;
                upFlag = Win32.MOUSEEVENTF_RIGHTUP;
                return true;
            case TriggerButton.Middle:
                downFlag = Win32.MOUSEEVENTF_MIDDLEDOWN;
                upFlag = Win32.MOUSEEVENTF_MIDDLEUP;
                return true;
            case TriggerButton.XButton1:
                downFlag = Win32.MOUSEEVENTF_XDOWN;
                upFlag = Win32.MOUSEEVENTF_XUP;
                mouseData = Win32.XBUTTON1;
                return true;
            case TriggerButton.XButton2:
                downFlag = Win32.MOUSEEVENTF_XDOWN;
                upFlag = Win32.MOUSEEVENTF_XUP;
                mouseData = Win32.XBUTTON2;
                return true;
            default:
                downFlag = 0;
                upFlag = 0;
                return false;
        }
    }

    private static Win32.INPUT MakeMouse(uint dwFlags, uint mouseData) => new()
    {
        type = Win32.INPUT_MOUSE,
        U = new Win32.INPUT.InputUnion
        {
            mi = new Win32.MOUSEINPUT
            {
                dwFlags = dwFlags,
                mouseData = mouseData,
                dwExtraInfo = Win32.INPUT_MARKER,
            },
        },
    };
}
