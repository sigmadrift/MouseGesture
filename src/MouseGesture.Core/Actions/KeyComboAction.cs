using System.Collections.Immutable;
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

        Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
    }

    private static Win32.INPUT MakeKey(VirtualKey vk, bool isUp) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.INPUT.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = (ushort)vk,
                dwFlags = isUp ? Win32.KEYEVENTF_KEYUP : 0,
                dwExtraInfo = Win32.INPUT_MARKER,
            },
        },
    };
}
