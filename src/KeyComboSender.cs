using System;
using System.Runtime.InteropServices;

public static class KeyComboSender {
    [DllImport("user32.dll")]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    const int INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;
    const uint KEYEVENTF_UNICODE = 0x0004;
    const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public UIntPtr extraInfo;
    }

    static bool IsExtended(ushort vk) {
        return vk == 0x21 || vk == 0x22 || vk == 0x23 || vk == 0x24 ||
               vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 ||
               vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C ||
               vk == 0x5D || vk == 0xA3 || vk == 0xA5;
    }

    static INPUT MakeInput(ushort vk, bool down) {
        uint flags = KEYEVENTF_SCANCODE;
        if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= KEYEVENTF_KEYUP;
        return new INPUT {
            type = INPUT_KEYBOARD,
            keyboard = new KEYBDINPUT {
                virtualKey = vk,
                scanCode = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC),
                flags = flags
            }
        };
    }

    public static bool Press(ushort[] combo) {
        if (combo == null || combo.Length == 0) return false;
        var inputs = new INPUT[combo.Length];
        for (int i = 0; i < combo.Length; i++) inputs[i] = MakeInput(combo[i], true);
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
    }

    public static bool Release(ushort[] combo) {
        if (combo == null || combo.Length == 0) return false;
        var inputs = new INPUT[combo.Length];
        for (int i = 0; i < combo.Length; i++) inputs[i] = MakeInput(combo[combo.Length - 1 - i], false);
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
    }

    static INPUT MakeUnicode(char ch, bool down) {
        uint flags = KEYEVENTF_UNICODE;
        if (!down) flags |= KEYEVENTF_KEYUP;
        return new INPUT {
            type = INPUT_KEYBOARD,
            keyboard = new KEYBDINPUT {
                virtualKey = 0,
                scanCode = ch,
                flags = flags
            }
        };
    }

    public static bool TypeText(string text) {
        if (String.IsNullOrEmpty(text)) return false;
        var inputs = new INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++) {
            inputs[i * 2] = MakeUnicode(text[i], true);
            inputs[i * 2 + 1] = MakeUnicode(text[i], false);
        }
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == (uint)inputs.Length;
    }

    public static bool Tap(ushort[] combo) {
        if (combo == null || combo.Length == 0) return false;
        var inputs = new INPUT[combo.Length * 2];
        for (int i = 0; i < combo.Length; i++) inputs[i] = MakeInput(combo[i], true);
        for (int i = 0; i < combo.Length; i++) inputs[combo.Length + i] = MakeInput(combo[combo.Length - 1 - i], false);
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) == inputs.Length;
    }
}
