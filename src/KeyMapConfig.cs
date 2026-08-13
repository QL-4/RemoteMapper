using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public enum KeyActionKind { Combo, TaskView, Launch, Cmd, Code }

public sealed class KeyBinding {
    public string Name { get; set; }
    public ushort SourceVk { get; set; }
    public ushort[] Combo { get; set; }
    public bool Tap { get; set; }
    public KeyActionKind ClickAction { get; set; }
    public string ClickCommand { get; set; }

    public uint LongPressMs { get; set; }
    public ushort[] LongCombo { get; set; }
    public KeyActionKind LongAction { get; set; }
    public string LongCommand { get; set; }

    public uint DoubleMs { get; set; }
    public ushort[] DoubleCombo { get; set; }
    public KeyActionKind DoubleAction { get; set; }
    public string DoubleCommand { get; set; }

    public uint RepeatDelay { get; set; }
    public uint RepeatInterval { get; set; }
    public ushort[] RepeatCombo { get; set; }
    public KeyActionKind RepeatAction { get; set; }
    public string RepeatCommand { get; set; }

    public KeyBinding() {
        Combo = new ushort[0];
    }

    public KeyBinding(string name, ushort sourceVk, ushort[] combo)
        : this(name, sourceVk, combo, false, 0, null) { }

    public KeyBinding(string name, ushort sourceVk, ushort[] combo, bool tap, uint longPressMs, ushort[] longCombo)
        : this(name, sourceVk, combo, tap, longPressMs, longCombo, KeyActionKind.Combo) { }

    public KeyBinding(string name, ushort sourceVk, ushort[] combo, bool tap, uint longPressMs, ushort[] longCombo, KeyActionKind longAction) {
        Name = name;
        SourceVk = sourceVk;
        Combo = combo ?? new ushort[0];
        Tap = tap;
        LongPressMs = longPressMs;
        LongCombo = longCombo;
        LongAction = longAction;
    }

    public bool HasClick { get { return HasPayload(ClickAction, Combo, ClickCommand); } }
    public bool HasDouble { get { return DoubleMs > 0 && HasPayload(DoubleAction, DoubleCombo, DoubleCommand); } }
    public bool HasLong { get { return LongPressMs > 0 && HasPayload(LongAction, LongCombo, LongCommand); } }
    public bool HasRepeat { get { return RepeatDelay > 0 && HasPayload(RepeatAction, RepeatCombo, RepeatCommand); } }
    public bool HasAny { get { return HasClick || HasDouble || HasLong || HasRepeat; } }

    public static bool HasPayload(KeyActionKind kind, ushort[] combo, string command) {
        if (kind == KeyActionKind.TaskView) return true;
        if (kind == KeyActionKind.Launch || kind == KeyActionKind.Cmd || kind == KeyActionKind.Code)
            return !String.IsNullOrWhiteSpace(command);
        return combo != null && combo.Length > 0;
    }
}

public static class KeyMapConfig {
    public const uint DefaultDoubleMs = 300;
    public const uint DefaultHoldMs = 600;
    public const uint DefaultRepeatDelay = 600;
    public const uint DefaultRepeatInterval = 100;

    static readonly Dictionary<string, ushort> KeyNames = BuildKeyNames();
    static readonly Dictionary<ushort, string> VkNames = BuildVkNames(KeyNames);

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

            string[] sourceParts = sourceText.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (sourceParts.Length == 0) continue;

            ushort sourceVk;
            if (!TryParseKey(sourceParts[0], out sourceVk))
                throw new FormatException("Unknown source key in keymap: " + sourceParts[0]);

            var binding = new KeyBinding(name, sourceVk, new ushort[0], false, 0, null, KeyActionKind.Combo);
            if (targetText.Length == 0) {
                result.Add(binding);
                continue;
            }

            string[] segments = targetText.Split('|');
            string clickText = segments[0].Trim();
            if (clickText.Length > 0) {
                bool tap = StripPrefix(ref clickText, "TAP");
                KeyActionKind clickAction;
                string clickCommand;
                ushort[] combo = ParseActionTarget(clickText, out clickAction, out clickCommand);
                binding.Tap = tap;
                binding.ClickAction = clickAction;
                binding.ClickCommand = clickCommand;
                binding.Combo = combo;
            }

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

            if (binding.HasLong && binding.HasRepeat)
                throw new FormatException("HOLD and REPEAT are mutually exclusive in keymap: " + line);

            result.Add(binding);
        }
        return result;
    }

    public static bool TryParseEnabled(IEnumerable<string> lines, out bool enabled) {
        enabled = true;
        if (lines == null) return false;
        foreach (string original in lines) {
            if (original == null) continue;
            string line = original.Trim();
            if (!line.StartsWith("#")) continue;
            int idx = line.IndexOf("mapping-enabled", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            int colon = line.IndexOf(':', idx);
            if (colon < 0) continue;
            string value = line.Substring(colon + 1).Trim();
            enabled = value != "0" && !String.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            return true;
        }
        return false;
    }

    static void ParseSegmentDouble(string seg, KeyBinding b) {
        int arrowPos = seg.IndexOf("->", StringComparison.Ordinal);
        if (arrowPos < 0)
            throw new FormatException("Expected -> after DOUBLE in keymap: " + seg);
        string msText = seg.Substring(7, arrowPos - 7).Trim();
        uint ms;
        if (!UInt32.TryParse(msText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms) || ms == 0)
            throw new FormatException("Invalid DOUBLE timeout in keymap: " + msText);
        KeyActionKind action;
        string command;
        ushort[] combo = ParseActionTarget(seg.Substring(arrowPos + 2), out action, out command);
        b.DoubleMs = ms;
        b.DoubleCombo = combo;
        b.DoubleAction = action;
        b.DoubleCommand = command;
    }

    static void ParseSegmentHold(string seg, KeyBinding b) {
        int arrowPos = seg.IndexOf("->", StringComparison.Ordinal);
        if (arrowPos < 0)
            throw new FormatException("Expected -> after HOLD in keymap: " + seg);
        string msText = seg.Substring(5, arrowPos - 5).Trim();
        uint ms;
        if (!UInt32.TryParse(msText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ms) || ms == 0)
            throw new FormatException("Invalid HOLD threshold in keymap: " + msText);
        KeyActionKind action;
        string command;
        ushort[] combo = ParseActionTarget(seg.Substring(arrowPos + 2), out action, out command);
        b.LongPressMs = ms;
        b.LongCombo = combo;
        b.LongAction = action;
        b.LongCommand = command;
    }

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
        string command;
        ushort[] combo = ParseActionTarget(seg.Substring(arrowPos + 2), out action, out command);
        b.RepeatDelay = delay;
        b.RepeatInterval = interval;
        b.RepeatCombo = combo;
        b.RepeatAction = action;
        b.RepeatCommand = command;
    }

    static ushort[] ParseActionTarget(string text, out KeyActionKind action, out string command) {
        action = KeyActionKind.Combo;
        command = null;
        text = text.Trim();
        if (String.Equals(text, "TASKVIEW", StringComparison.OrdinalIgnoreCase)) {
            action = KeyActionKind.TaskView;
            return new ushort[0];
        }
        if (StartsWithWord(text, "CODE")) {
            action = KeyActionKind.Code;
            command = text.Substring(4).Trim();
            if (command.Length == 0)
                throw new FormatException("CODE requires a snippet");
            return new ushort[0];
        }
        if (StartsWithWord(text, "LAUNCH")) {
            action = KeyActionKind.Launch;
            command = text.Substring(6).Trim();
            if (command.Length == 0)
                throw new FormatException("LAUNCH requires a path");
            return new ushort[0];
        }
        if (StartsWithWord(text, "CMD")) {
            action = KeyActionKind.Cmd;
            command = text.Substring(3).Trim();
            if (command.Length == 0)
                throw new FormatException("CMD requires a command");
            return new ushort[0];
        }
        StripPrefix(ref text, "TAP");
        ushort[] combo = ParseCombo(text);
        if (combo.Length == 0)
            throw new FormatException("Empty action target in keymap: " + text);
        return combo;
    }

    static bool StartsWithWord(string text, string word) {
        if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
        return text.Length == word.Length || Char.IsWhiteSpace(text[word.Length]);
    }

    static bool StripPrefix(ref string text, string prefix) {
        if (!text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) return false;
        text = text.Substring(prefix.Length + 1).Trim();
        return true;
    }

    public static bool TryParseCombo(string text, out ushort[] combo) {
        combo = new ushort[0];
        if (String.IsNullOrWhiteSpace(text)) return false;
        try {
            combo = ParseCombo(text);
            return combo.Length > 0;
        } catch {
            return false;
        }
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

    public static string FormatCombo(ushort[] combo) {
        if (combo == null || combo.Length == 0) return "";
        var parts = new string[combo.Length];
        for (int i = 0; i < combo.Length; i++) {
            string name;
            parts[i] = VkNames.TryGetValue(combo[i], out name) ? name : ("0x" + combo[i].ToString("X2"));
        }
        return String.Join("+", parts);
    }

    public static string FormatAction(KeyActionKind kind, ushort[] combo, string command, bool tap) {
        if (kind == KeyActionKind.TaskView) return "TASKVIEW";
        if (kind == KeyActionKind.Launch) return "LAUNCH " + command;
        if (kind == KeyActionKind.Code) return "CODE " + command;
        if (kind == KeyActionKind.Cmd) return "CMD " + command;
        string body = FormatCombo(combo);
        if (body.Length == 0) return "";
        return tap ? ("TAP " + body) : body;
    }

    public static string FormatDisplay(KeyActionKind kind, ushort[] combo, string command) {
        if (!KeyBinding.HasPayload(kind, combo, command)) return "未设置";
        if (kind == KeyActionKind.TaskView) return "任务视图";
        if (kind == KeyActionKind.Launch) return "打开 " + LaunchDisplayName(command);
        if (kind == KeyActionKind.Code) return "代码";
        if (kind == KeyActionKind.Cmd) return command;
        if (combo == null || combo.Length == 0) return "未设置";
        var parts = new string[combo.Length];
        for (int i = 0; i < combo.Length; i++) parts[i] = FriendlyVk(combo[i]);
        return String.Join("+", parts);
    }

    public static string LaunchDisplayName(string command) {
        if (String.IsNullOrWhiteSpace(command)) return "";
        string path, args;
        SplitCommand(command, out path, out args);
        try { return Path.GetFileNameWithoutExtension(path); }
        catch { return path; }
    }

    public static void SplitCommand(string command, out string path, out string args) {
        path = "";
        args = "";
        if (String.IsNullOrWhiteSpace(command)) return;
        command = command.Trim();
        if (command[0] == '"') {
            int end = command.IndexOf('"', 1);
            if (end > 0) {
                path = command.Substring(1, end - 1);
                args = end + 1 < command.Length ? command.Substring(end + 1).Trim() : "";
                return;
            }
        }
        int space = command.IndexOf(' ');
        if (space < 0) { path = command; return; }
        path = command.Substring(0, space);
        args = command.Substring(space + 1).Trim();
    }

    public static string FormatLine(KeyBinding b) {
        string name = (b.Name ?? "").PadRight(8);
        string vk = "0x" + b.SourceVk.ToString("X2");
        if (!b.HasAny) return name + " = " + vk + " ->";

        var parts = new List<string>();
        if (b.HasClick)
            parts.Add(FormatAction(b.ClickAction, b.Combo, b.ClickCommand, b.Tap));

        if (b.HasDouble)
            parts.Add("DOUBLE " + b.DoubleMs + " -> " + FormatAction(b.DoubleAction, b.DoubleCombo, b.DoubleCommand, true));

        if (b.HasLong)
            parts.Add("HOLD " + b.LongPressMs + " -> " + FormatAction(b.LongAction, b.LongCombo, b.LongCommand, true));

        if (b.HasRepeat)
            parts.Add("REPEAT " + b.RepeatDelay + " " + b.RepeatInterval + " -> " + FormatAction(b.RepeatAction, b.RepeatCombo, b.RepeatCommand, true));

        if (parts.Count == 0) return name + " = " + vk + " ->";
        return name + " = " + vk + " -> " + String.Join(" | ", parts.ToArray());
    }

    public static string[] BuildHeader(bool enabled) {
        return new[] {
            "# RemoteMapper keymap config",
            "# mapping-enabled: " + (enabled ? "1" : "0"),
            "# 由 RemoteMic 控制面板读写；保存后立即热加载，无需重启。",
            "# 驱动把可配置遥控器键变成 F13-F19；语音键为 F20，由 VoiceKeyBlocker 吞掉。",
            "# 方向键和确定键保持系统原行为，不要写成组合键映射源。",
            "#",
            "# 格式: <键名> = <vk> -> <目标>",
            "# 目标: TAP LCTRL+Z  /  TASKVIEW  /  LAUNCH C:\\App\\app.exe  /  CMD start ms-settings:network",
            "# 手势: DOUBLE <ms> -> <动作>  |  HOLD <ms> -> <动作>  |  REPEAT <延迟> <间隔> -> <动作>",
            "# HOLD 与 REPEAT 互斥。-> 后留空 = 不映射。"
        };
    }

    public static void WriteFile(string path, IList<KeyBinding> bindings, bool enabled) {
        if (path != null && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            WriteJsonFile(path, bindings, enabled);
            return;
        }
        var lines = new List<string>();
        lines.AddRange(BuildHeader(enabled));
        lines.Add("");
        foreach (KeyBinding b in bindings) lines.Add(FormatLine(b));
        File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(true));
    }

    public class JsonFile {
        public bool enabled = true;
        public List<JsonKey> keys = new List<JsonKey>();
    }
    public class JsonKey {
        public string id;
        public string name;
        public string vk;
        public JsonAction click;
        public JsonAction dbl;
        public JsonAction hold;
        public JsonAction repeat;
    }
    public class JsonAction {
        public string kind;
        public bool tap;
        public string keys;
        public string command;
        public uint ms;
        public uint delay;
        public uint interval;
    }

    public static List<KeyBinding> ReadJsonFile(string path, out bool enabled) {
        string text = File.ReadAllText(path, Encoding.UTF8);
        return ReadJson(text, out enabled);
    }

    public static List<KeyBinding> ReadJson(string text, out bool enabled) {
        enabled = true;
        var ser = new JavaScriptSerializer();
        JsonFile file = ser.Deserialize<JsonFile>(text);
        if (file == null) return new List<KeyBinding>();
        enabled = file.enabled;
        var result = new List<KeyBinding>();
        if (file.keys == null) return result;
        foreach (JsonKey k in file.keys) {
            if (k == null) continue;
            var b = new KeyBinding();
            b.Name = k.name;
            ushort vk;
            if (!TryParseKey(k.vk, out vk) && (k.vk == null || !UInt16.TryParse(k.vk, NumberStyles.Integer, CultureInfo.InvariantCulture, out vk)))
                throw new FormatException("Unknown vk in keymap.json: " + k.vk);
            b.SourceVk = vk;
            ApplyJsonAction(b, k.click, "click");
            ApplyJsonAction(b, k.dbl, "dbl");
            ApplyJsonAction(b, k.hold, "hold");
            ApplyJsonAction(b, k.repeat, "repeat");
            result.Add(b);
        }
        return result;
    }

    static void ApplyJsonAction(KeyBinding b, JsonAction a, string which) {
        if (a == null || String.IsNullOrEmpty(a.kind) || a.kind == "empty") return;
        KeyActionKind kind = ParseKind(a.kind);
        ushort[] combo = new ushort[0];
        if (kind == KeyActionKind.Combo && !String.IsNullOrWhiteSpace(a.keys)) {
            ushort[] parsed;
            if (!TryParseCombo(a.keys, out parsed))
                throw new FormatException("Unknown combo in keymap.json: " + a.keys);
            combo = parsed;
        }
        string command = a.command;
        if (kind == KeyActionKind.Code && String.IsNullOrWhiteSpace(command))
            command = "DateTime.Now.ToString(\"HH:mm\")";
        if (which == "click") {
            b.ClickAction = kind;
            b.Combo = combo;
            b.ClickCommand = command;
            b.Tap = a.tap || kind != KeyActionKind.Combo || combo.Length > 1;
        } else if (which == "dbl") {
            b.DoubleAction = kind;
            b.DoubleCombo = combo;
            b.DoubleCommand = command;
            b.DoubleMs = a.ms > 0 ? a.ms : DefaultDoubleMs;
        } else if (which == "hold") {
            b.LongAction = kind;
            b.LongCombo = combo;
            b.LongCommand = command;
            b.LongPressMs = a.ms > 0 ? a.ms : DefaultHoldMs;
        } else if (which == "repeat") {
            b.RepeatAction = kind;
            b.RepeatCombo = combo;
            b.RepeatCommand = command;
            b.RepeatDelay = a.delay > 0 ? a.delay : DefaultRepeatDelay;
            b.RepeatInterval = a.interval > 0 ? a.interval : DefaultRepeatInterval;
        }
    }

    static KeyActionKind ParseKind(string kind) {
        if (String.Equals(kind, "taskview", StringComparison.OrdinalIgnoreCase)) return KeyActionKind.TaskView;
        if (String.Equals(kind, "code", StringComparison.OrdinalIgnoreCase)) return KeyActionKind.Code;
        if (String.Equals(kind, "time", StringComparison.OrdinalIgnoreCase)) return KeyActionKind.Code;
        if (String.Equals(kind, "launch", StringComparison.OrdinalIgnoreCase)) return KeyActionKind.Launch;
        if (String.Equals(kind, "cmd", StringComparison.OrdinalIgnoreCase)) return KeyActionKind.Cmd;
        return KeyActionKind.Combo;
    }

    static string KindName(KeyActionKind kind) {
        if (kind == KeyActionKind.TaskView) return "taskview";
        if (kind == KeyActionKind.Code) return "code";
        if (kind == KeyActionKind.Launch) return "launch";
        if (kind == KeyActionKind.Cmd) return "cmd";
        return "combo";
    }

    static JsonAction ToJsonAction(KeyActionKind kind, ushort[] combo, string command, bool tap, uint ms, uint delay, uint interval) {
        if (!KeyBinding.HasPayload(kind, combo, command)) return null;
        var a = new JsonAction();
        a.kind = KindName(kind);
        a.tap = tap;
        if (kind == KeyActionKind.Combo) a.keys = FormatCombo(combo);
        if (kind == KeyActionKind.Launch || kind == KeyActionKind.Cmd || kind == KeyActionKind.Code) a.command = command;
        if (ms > 0) a.ms = ms;
        if (delay > 0) a.delay = delay;
        if (interval > 0) a.interval = interval;
        return a;
    }

    public static void WriteJsonFile(string path, IList<KeyBinding> bindings, bool enabled) {
        var file = new JsonFile();
        file.enabled = enabled;
        file.keys = new List<JsonKey>();
        foreach (KeyBinding b in bindings) {
            var k = new JsonKey();
            RemoteKeyDef def = RemoteCatalog.FindByVk(b.SourceVk);
            k.id = def != null ? def.Id : null;
            k.name = b.Name;
            k.vk = "0x" + b.SourceVk.ToString("X2");
            k.click = ToJsonAction(b.ClickAction, b.Combo, b.ClickCommand, b.Tap, 0, 0, 0);
            k.dbl = b.HasDouble ? ToJsonAction(b.DoubleAction, b.DoubleCombo, b.DoubleCommand, true, b.DoubleMs, 0, 0) : null;
            k.hold = b.HasLong ? ToJsonAction(b.LongAction, b.LongCombo, b.LongCommand, true, b.LongPressMs, 0, 0) : null;
            k.repeat = b.HasRepeat ? ToJsonAction(b.RepeatAction, b.RepeatCombo, b.RepeatCommand, true, 0, b.RepeatDelay, b.RepeatInterval) : null;
            file.keys.Add(k);
        }
        var ser = new JavaScriptSerializer();
        File.WriteAllText(path, PrettyJson(ser.Serialize(file)), new UTF8Encoding(true));
    }

    public static string PrettyJson(string json) {
        var sb = new StringBuilder();
        int indent = 0;
        bool inStr = false;
        for (int i = 0; i < json.Length; i++) {
            char c = json[i];
            if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
            if (!inStr && (c == '{' || c == '[')) {
                sb.Append(c);
                sb.Append('\n');
                indent++;
                sb.Append(' ', indent * 2);
            } else if (!inStr && (c == '}' || c == ']')) {
                sb.Append('\n');
                indent--;
                sb.Append(' ', indent * 2);
                sb.Append(c);
            } else if (!inStr && c == ',') {
                sb.Append(c);
                sb.Append('\n');
                sb.Append(' ', indent * 2);
            } else if (!inStr && c == ':') {
                sb.Append(": ");
            } else {
                sb.Append(c);
            }
        }
        sb.Append('\n');
        return sb.ToString();
    }

    public static string FriendlyVk(ushort vk) {
        switch (vk) {
            case 0x08: return "Backspace";
            case 0x09: return "Tab";
            case 0x0D: return "Enter";
            case 0x1B: return "Esc";
            case 0x20: return "Space";
            case 0x2E: return "Delete";
            case 0x5B: return "Win";
            case 0x5C: return "RWin";
            case 0xA0: return "Shift";
            case 0xA1: return "RShift";
            case 0xA2: return "Ctrl";
            case 0xA3: return "RCtrl";
            case 0xA4: return "Alt";
            case 0xA5: return "RAlt";
            case 0xBC: return ",";
            case 0xBE: return ".";
            default:
                string name;
                if (VkNames.TryGetValue(vk, out name)) return name;
                return "0x" + vk.ToString("X2");
        }
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

    static Dictionary<ushort, string> BuildVkNames(Dictionary<string, ushort> names) {
        var d = new Dictionary<ushort, string>();
        foreach (var pair in names) {
            if (!d.ContainsKey(pair.Value)) d[pair.Value] = pair.Key;
        }
        return d;
    }
}
