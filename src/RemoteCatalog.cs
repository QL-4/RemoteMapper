using System;
using System.Collections.Generic;

public enum RemoteKeyMode { Editable, Native, Voice }

public sealed class RemoteKeyDef {
    public string Id;
    public string Name;
    public string Title;
    public ushort SourceVk;
    public RemoteKeyMode Mode;

    public RemoteKeyDef(string id, string name, string title, ushort vk, RemoteKeyMode mode) {
        Id = id;
        Name = name;
        Title = title;
        SourceVk = vk;
        Mode = mode;
    }
}

public static class RemoteCatalog {
    public static readonly RemoteKeyDef[] All = {
        new RemoteKeyDef("power", "电源键", "电源键", 0x82, RemoteKeyMode.Editable),
        new RemoteKeyDef("ok", "确定键", "确定键", 0x0D, RemoteKeyMode.Native),
        new RemoteKeyDef("up", "方向上", "上键", 0x26, RemoteKeyMode.Native),
        new RemoteKeyDef("down", "方向下", "下键", 0x28, RemoteKeyMode.Native),
        new RemoteKeyDef("left", "方向左", "左键", 0x25, RemoteKeyMode.Native),
        new RemoteKeyDef("right", "方向右", "右键", 0x27, RemoteKeyMode.Native),
        new RemoteKeyDef("back", "返回键", "返回键", 0x7E, RemoteKeyMode.Editable),
        new RemoteKeyDef("home", "主页键", "主页键", 0x7F, RemoteKeyMode.Editable),
        new RemoteKeyDef("volup", "音量加", "音量 +", 0x7C, RemoteKeyMode.Editable),
        new RemoteKeyDef("voldown", "音量减", "音量 -", 0x7D, RemoteKeyMode.Editable),
        new RemoteKeyDef("menu", "菜单键", "菜单键", 0x80, RemoteKeyMode.Editable),
        new RemoteKeyDef("tv", "直播键", "TV 键", 0x81, RemoteKeyMode.Editable),
        new RemoteKeyDef("voice", "语音键", "语音键", 0x83, RemoteKeyMode.Voice)
    };

    public static readonly RemoteKeyDef[] FileOrder = {
        All[0], All[1], All[2], All[3], All[4], All[5],
        All[6], All[7], All[8], All[9], All[10], All[11]
    };

    public static List<KeyBinding> DefaultBindings() {
        return new List<KeyBinding> {
            Parse("电源键    = 0x82 -> TAP LALT+TAB | HOLD 600 -> TASKVIEW"),
            Parse("确定键    = 0x0D ->"),
            Parse("方向上    = 0x26 -> UP"),
            Parse("方向下    = 0x28 -> DOWN"),
            Parse("方向左    = 0x25 -> LEFT"),
            Parse("方向右    = 0x27 -> RIGHT"),
            Parse("返回键    = 0x7E -> TAP LCTRL+Z | HOLD 600 -> TAP LCTRL+LSHIFT+Z"),
            Parse("主页键    = 0x7F -> TAP LALT+X | HOLD 600 -> TAP LALT+F4"),
            Parse("音量加    = 0x7C -> TAP BACK | REPEAT 600 100 -> TAP BACK"),
            Parse("音量减    = 0x7D -> DELETE"),
            Parse("菜单键    = 0x80 ->"),
            Parse("直播键    = 0x81 -> ESC")
        };
    }

    public static RemoteKeyDef FindById(string id) {
        if (id == null) return null;
        foreach (RemoteKeyDef def in All)
            if (String.Equals(def.Id, id, System.StringComparison.OrdinalIgnoreCase))
                return def;
        return null;
    }

    public static RemoteKeyDef FindByVk(ushort vk) {
        foreach (RemoteKeyDef def in All)
            if (def.SourceVk == vk) return def;
        return null;
    }

    public static List<KeyBinding> Merge(IList<KeyBinding> loaded) {
        var byVk = new Dictionary<ushort, KeyBinding>();
        if (loaded != null) {
            foreach (KeyBinding b in loaded) byVk[b.SourceVk] = b;
        }
        var result = new List<KeyBinding>();
        foreach (RemoteKeyDef def in FileOrder) {
            KeyBinding b;
            if (byVk.TryGetValue(def.SourceVk, out b)) {
                if (string.IsNullOrEmpty(b.Name)) b.Name = def.Name;
                result.Add(b);
            } else {
                result.Add(new KeyBinding(def.Name, def.SourceVk, new ushort[0]));
            }
        }
        return result;
    }

    static KeyBinding Parse(string line) {
        return KeyMapConfig.ParseLines(new[] { line })[0];
    }
}
