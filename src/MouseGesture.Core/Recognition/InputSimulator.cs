using System.Runtime.InteropServices;
using MouseGesture.Core.Native;

namespace MouseGesture.Core.Recognition;

/// <summary>
/// Synthesizes mouse input on behalf of the app. All injected events carry
/// <see cref="Win32.INPUT_MARKER"/> in dwExtraInfo so our own hook ignores them
/// (preventing re-entrant capture).
/// </summary>
internal static class InputSimulator
{
    /// <summary>Sends a full down+up click for the given button (e.g. to pass a plain click through).</summary>
    public static void ButtonClick(TriggerButton button)
    {
        if (!TryGetFlags(button, out var downFlag, out var upFlag, out var mouseData))
            return;

        var inputs = new[]
        {
            MakeMouse(downFlag, mouseData),
            MakeMouse(upFlag, mouseData),
        };
        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
    }

    /// <summary>
    /// Sends <paramref name="count"/> discrete wheel events of <paramref name="notchDelta"/>
    /// each, in a single batched SendInput call. Emitting separate notches (rather than one
    /// big delta) makes apps that ignore the delta magnitude — and only scroll a fixed step
    /// per WM_MOUSEWHEEL — still amplify; apps that respect the magnitude get the same total.
    /// Must be called off the hook thread; one batch is one syscall regardless of count.
    /// </summary>
    public static void Wheel(int notchDelta, int count)
    {
        if (count < 1 || notchDelta == 0)
            return;

        var inputs = new Win32.INPUT[count];
        for (var i = 0; i < count; i++)
            inputs[i] = MakeMouse(Win32.MOUSEEVENTF_WHEEL, unchecked((uint)notchDelta));

        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
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
