using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseGesture.Core.Native;

namespace MouseGesture.Core.Actions;

/// <summary>Sends a single chord: holds modifiers, taps the main key, releases modifiers.</summary>
public sealed class KeyComboAction : IGestureAction
{
    private readonly ImmutableArray<VirtualKey> _modifiers;
    private readonly VirtualKey _mainKey;

    public string Id { get; }
    public string Name { get; }

    public KeyComboAction(string id, string name, VirtualKey mainKey, params VirtualKey[] modifiers)
    {
        Id = id;
        Name = name;
        _mainKey = mainKey;
        _modifiers = [.. modifiers];
    }

    public void Execute()
    {
        var count = _modifiers.Length * 2 + 2;
        var inputs = new Win32.INPUT[count];
        var i = 0;
        foreach (var mod in _modifiers)
            inputs[i++] = MakeKey(mod, isUp: false);
        inputs[i++] = MakeKey(_mainKey, isUp: false);
        inputs[i++] = MakeKey(_mainKey, isUp: true);
        for (var m = _modifiers.Length - 1; m >= 0; m--)
            inputs[i++] = MakeKey(_modifiers[m], isUp: true);

        var sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
        if (sent != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput for '{Id}' was blocked or failed.");
    }

    private static Win32.INPUT MakeKey(VirtualKey vk, bool isUp)
    {
        // Provide the real scan code (and the extended flag for E0-prefixed keys such as
        // the arrows and Win): apps that read scan codes — RDP, VMs, games — would
        // otherwise ignore the key or see numpad arrows instead.
        var sc = Win32.MapVirtualKey((uint)vk, Win32.MAPVK_VK_TO_VSC_EX);
        var extended = (sc & 0xFF00) is 0xE000 or 0xE100 || IsExtendedKey(vk);
        var flags = (isUp ? Win32.KEYEVENTF_KEYUP : 0) | (extended ? Win32.KEYEVENTF_EXTENDEDKEY : 0);
        return new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            U = new Win32.INPUT.InputUnion
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)(sc & 0xFF),
                    dwFlags = flags,
                    dwExtraInfo = Win32.INPUT_MARKER,
                },
            },
        };
    }

    private static bool IsExtendedKey(VirtualKey vk) => vk is
        VirtualKey.Left or VirtualKey.Up or VirtualKey.Right or VirtualKey.Down or
        VirtualKey.LWin or
        VirtualKey.BrowserBack or VirtualKey.BrowserForward or VirtualKey.BrowserRefresh or
        VirtualKey.VolumeMute or VirtualKey.VolumeDown or VirtualKey.VolumeUp or
        VirtualKey.MediaNextTrack or VirtualKey.MediaPrevTrack or VirtualKey.MediaStop or VirtualKey.MediaPlayPause;
}
