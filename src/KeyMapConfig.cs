using System;
using System.Collections.Generic;
using System.Globalization;

public enum KeyActionKind { Combo, TaskView }

public sealed class KeyBinding {
    public string Name { get; set; }
    public ushort SourceVk { get; set; }
    public ushort[] Combo { get; set; }
    public bool Tap { get; set; }

    // Long press (mutually exclusive with Repeat)
    public uint LongPressMs { get; set; }
    public ushort[] LongCombo { get; set; }
    public KeyActionKind LongAction { get; set; }

    // Double click
    public uint DoubleMs { get; set; }
    public ushort[] DoubleCombo { get; set; }
    public KeyActionKind DoubleAction { get; set; }

    // Hold-repeat (mutually exclusive with Long)
    public uint RepeatDelay { get; set; }
    public uint RepeatInterval { get; set; }
    public ushort[] RepeatCombo { get; set; }
    public KeyActionKind RepeatAction { get; set; }

    public KeyBinding() { }

    public KeyBinding(string name, ushort sourceVk, ushort[] combo)
        : this(name, sourceVk, combo, false, 0, null) { }

    public KeyBinding(string name, ushort sourceVk, ushort[] combo, bool tap, uint longPressMs, ushort[] longCombo)
        : this(name, sourceVk, combo, tap, longPressMs, longCombo, KeyActionKind.Combo) { }

    public KeyBinding(string name, ushort sourceVk, ushort[] combo, bool tap, uint longPressMs, ushort[] longCombo, KeyActionKind longAction) {
        Name = name;
        SourceVk = sourceVk;
        Combo = combo;
        Tap = tap;
        LongPressMs = longPressMs;
        LongCombo = longCombo;
        LongAction = longAction;
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

            // Split on | to separate click target from gesture segments (DOUBLE/HOLD/REPEAT)
            string[] segments = targetText.Split('|');
            string clickText = segments[0].Trim();

            string[] sourceParts = sourceText.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (sourceParts.Length == 0) continue;

            ushort sourceVk;
            if (!TryParseKey(sourceParts[0], out sourceVk))
                throw new FormatException("Unknown source key in keymap: " + sourceParts[0]);

            bool tap = StripPrefix(ref clickText, "TAP");
            ushort[] combo = ParseCombo(clickText);
            if (combo.Length == 0) continue;

            var binding = new KeyBinding(name, sourceVk, combo, tap, 0, null, KeyActionKind.Combo);

            // Parse gesture segments
            for (int i = 1; i < segments.Length; i++) {
                string seg = segments[i].Trim();
                if (seg.Length == 0) continue;

                if (seg.StartsWith("DOUBLE ", StringComparison.OrdinalIgnoreCase))
                    ParseSegmentDouble(seg, binding);
                else if (seg.StartsWith("HOLD ", StringComparison.OrdinalIgnoreCase))
                    ParseSegmentHold(seg, binding);
                else if (seg.StartsWith("REPEAT ", StringComparison.OrdinalIgnoreCase))
                    ParseSegmentRepeat(seg, binding);
                else
                    throw new FormatException("Unknown gesture in keymap: " + seg);
            }

            // HOLD and REPEAT are mutually exclusive
            if (binding.LongCombo != null && binding.RepeatCombo != null)
                throw new FormatException("HOLD and REPEAT are mutually exclusive in keymap: " + line);

            result.Add(binding);
        }
        return result;
    }

    // "DOUBLE <ms> -> <target>"
    static void ParseSegmentDouble(string seg, KeyBinding b) {
        int arrowPos = seg.IndexOf("->", StringComparison.Ordinal);
        if (arrowPos < 0)
            throw new FormatException("Expected -> after DOUBLE in keymap: " + seg);
        string msText = seg.Substring(7, arrowPos - 7).Trim();
        uint ms;
        if (!UInt32.TryParse(msText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms) || ms == 0)
            throw new FormatException("Invalid DOUBLE timeout in keymap: " + msText);
        KeyActionKind action;
        ushort[] combo = ParseActionTarget(seg.Substring(arrowPos + 2), out action);
        b.DoubleMs = ms;
        b.DoubleCombo = combo;
        b.DoubleAction = action;
    }

    // "HOLD <ms> -> <target>"
    static void ParseSegmentHold(string seg, KeyBinding b) {
        int arrowPos = seg.IndexOf("->", StringComparison.Ordinal);
        if (arrowPos < 0)
            throw new FormatException("Expected -> after HOLD in keymap: " + seg);
        string msText = seg.Substring(5, arrowPos - 5).Trim();
        uint ms;
        if (!UInt32.TryParse(msText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms) || ms == 0)
            throw new FormatException("Invalid HOLD threshold in keymap: " + msText);
        KeyActionKind action;
        ushort[] combo = ParseActionTarget(seg.Substring(arrowPos + 2), out action);
        b.LongPressMs = ms;
        b.LongCombo = combo;
        b.LongAction = action;
    }

    // "REPEAT <delay> <interval> -> <target>"
    static void ParseSegmentRepeat(string seg, KeyBinding b) {
        int arrowPos = seg.IndexOf("->", StringComparison.Ordinal);
        if (arrowPos < 0)
            throw new FormatException("Expected -> after REPEAT in keymap: " + seg);
        string nums = seg.Substring(7, arrowPos - 7).Trim();
        string[] parts = nums.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new FormatException("REPEAT requires <delay> <interval> in keymap: " + nums);
        uint delay, interval;
        if (!UInt32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out delay) || delay == 0)
            throw new FormatException("Invalid REPEAT delay in keymap: " + parts[0]);
        if (!UInt32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out interval) || interval == 0)
            throw new FormatException("Invalid REPEAT interval in keymap: " + parts[1]);
        KeyActionKind action;
        ushort[] combo = ParseActionTarget(seg.Substring(arrowPos + 2), out action);
        b.RepeatDelay = delay;
        b.RepeatInterval = interval;
        b.RepeatCombo = combo;
        b.RepeatAction = action;
    }

    // Parse a target: "TAP <combo>", "<combo>", or "TASKVIEW"
    static ushort[] ParseActionTarget(string text, out KeyActionKind action) {
        action = KeyActionKind.Combo;
        text = text.Trim();
        if (String.Equals(text, "TASKVIEW", StringComparison.OrdinalIgnoreCase)) {
            action = KeyActionKind.TaskView;
            return new ushort[0];
        }
        StripPrefix(ref text, "TAP");
        ushort[] combo = ParseCombo(text);
        if (combo.Length == 0)
            throw new FormatException("Empty action target in keymap: " + text);
        return combo;
    }

    static bool StripPrefix(ref string text, string prefix) {
        if (!text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) return false;
        text = text.Substring(prefix.Length + 1).Trim();
        return true;
    }

    static ushort[] ParseCombo(string text) {
        string[] parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
        var combo = new List<ushort>();
        foreach (string part in parts) {
            ushort vk;
            if (!TryParseKey(part.Trim(), out vk))
                throw new FormatException("Unknown target key in keymap: " + part.Trim());
            combo.Add(vk);
        }
        return combo.ToArray();
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
