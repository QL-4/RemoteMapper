using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class KeyBinding {
    public string Name { get; private set; }
    public ushort SourceVk { get; private set; }
    public ushort[] Combo { get; private set; }

    public KeyBinding(string name, ushort sourceVk, ushort[] combo) {
        Name = name;
        SourceVk = sourceVk;
        Combo = combo;
    }
}

public static class KeyMapConfig {
    static readonly Dictionary<string, ushort> KeyNames = BuildKeyNames();

    public static List<KeyBinding> ParseLines(IEnumerable<string> lines) {
        var result = new List<KeyBinding>();
        foreach (string original in lines) {
            string line = original == null ? "" : original.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            int equals = line.IndexOf('=');
            int arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (equals <= 0 || arrow <= equals) continue;

            string name = line.Substring(0, equals).Trim();
            string sourceText = line.Substring(equals + 1, arrow - equals - 1).Trim();
            string targetText = line.Substring(arrow + 2).Trim();
            if (targetText.Length == 0) continue;

            string[] sourceParts = sourceText.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (sourceParts.Length == 0) continue;

            ushort sourceVk;
            if (!TryParseKey(sourceParts[0], out sourceVk))
                throw new FormatException("Unknown source key in keymap: " + sourceParts[0]);

            string[] targetParts = targetText.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var combo = new List<ushort>();
            foreach (string part in targetParts) {
                ushort targetVk;
                if (!TryParseKey(part.Trim(), out targetVk))
                    throw new FormatException("Unknown target key in keymap: " + part.Trim());
                combo.Add(targetVk);
            }
            if (combo.Count == 0) continue;

            result.Add(new KeyBinding(name, sourceVk, combo.ToArray()));
        }
        return result;
    }

    public static bool TryParseKey(string text, out ushort vk) {
        vk = 0;
        if (String.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return UInt16.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk);

        return KeyNames.TryGetValue(text, out vk);
    }

    static Dictionary<string, ushort> BuildKeyNames() {
        var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (ushort c = (ushort)'A'; c <= (ushort)'Z'; c++) d[((char)c).ToString()] = c;
        for (ushort c = (ushort)'0'; c <= (ushort)'9'; c++) d[((char)c).ToString()] = c;
        for (ushort i = 1; i <= 24; i++) d["F" + i] = (ushort)(0x6F + i);

        d["BACK"] = 0x08; d["TAB"] = 0x09; d["ENTER"] = 0x0D;
        d["PAUSE"] = 0x13; d["CAPSLOCK"] = 0x14; d["ESC"] = 0x1B; d["SPACE"] = 0x20;
        d["PGUP"] = 0x21; d["PGDN"] = 0x22; d["END"] = 0x23; d["HOME"] = 0x24;
        d["LEFT"] = 0x25; d["UP"] = 0x26; d["RIGHT"] = 0x27; d["DOWN"] = 0x28;
        d["PRTSCR"] = 0x2C; d["INSERT"] = 0x2D; d["DELETE"] = 0x2E;
        d["LWIN"] = 0x5B; d["RWIN"] = 0x5C; d["MENU"] = 0x5D;
        d["LSHIFT"] = 0xA0; d["RSHIFT"] = 0xA1; d["LCTRL"] = 0xA2; d["RCTRL"] = 0xA3;
        d["LALT"] = 0xA4; d["RALT"] = 0xA5;
        d["OEM_1"] = 0xBA; d["OEM_PLUS"] = 0xBB; d["OEM_COMMA"] = 0xBC;
        d["OEM_MINUS"] = 0xBD; d["OEM_PERIOD"] = 0xBE; d["OEM_2"] = 0xBF;
        d["OEM_3"] = 0xC0; d["OEM_4"] = 0xDB; d["OEM_5"] = 0xDC;
        d["OEM_6"] = 0xDD; d["OEM_7"] = 0xDE;
        return d;
    }
}
