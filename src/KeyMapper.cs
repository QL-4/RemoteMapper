using System;
using System.Collections.Generic;
using System.IO;

public static class KeyMapper {
    static readonly object gate = new object();
    static KeyMapEngine engine = new KeyMapEngine(new KeyBinding[0]);
    static List<KeyBinding> loaded = new List<KeyBinding>();
    static string configPath = "keymap.json";
    static volatile bool enabled = true;

    public static string ConfigPath {
        get { lock (gate) return configPath; }
    }

    public static bool Enabled {
        get { return enabled; }
        set { enabled = value; }
    }

    public static int BindingCount {
        get { lock (gate) return engine.BindingCount; }
    }

    public static void Load(string path) {
        if (!Path.IsPathRooted(path))
            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        lock (gate) {
            configPath = path;
            try {
                string jsonPath = path;
                if (!jsonPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    jsonPath = Path.ChangeExtension(path, ".json");
                string txtPath = Path.ChangeExtension(jsonPath, ".txt");

                if (File.Exists(jsonPath)) {
                    bool jsonEnabled;
                    loaded = KeyMapConfig.ReadJsonFile(jsonPath, out jsonEnabled);
                    enabled = jsonEnabled;
                    configPath = jsonPath;
                } else if (File.Exists(txtPath)) {
                    string[] lines = File.ReadAllLines(txtPath);
                    bool parsedEnabled;
                    if (KeyMapConfig.TryParseEnabled(lines, out parsedEnabled))
                        enabled = parsedEnabled;
                    loaded = KeyMapConfig.ParseLines(lines);
                    configPath = jsonPath;
                    KeyMapConfig.WriteJsonFile(jsonPath, loaded, enabled);
                    Console.WriteLine("[KEYMAP] migrated " + txtPath + " -> " + jsonPath);
                } else {
                    loaded = new List<KeyBinding>();
                    engine = new KeyMapEngine(new KeyBinding[0]);
                    enabled = true;
                    Console.WriteLine("[KEYMAP] config not found: " + jsonPath);
                    return;
                }

                engine = new KeyMapEngine(loaded);
                Console.WriteLine("[KEYMAP] loaded " + engine.BindingCount + " active mapping(s) from " + configPath
                    + (enabled ? "" : " (disabled)"));
            } catch (Exception ex) {
                loaded = new List<KeyBinding>();
                engine = new KeyMapEngine(new KeyBinding[0]);
                Console.WriteLine("[KEYMAP] disabled: " + ex.Message);
            }
        }
    }

    public static void Reload() {
        string path;
        lock (gate) path = configPath;
        Load(path);
    }

    public static void Replace(IEnumerable<KeyBinding> bindings) {
        lock (gate) {
            loaded = new List<KeyBinding>(bindings);
            engine = new KeyMapEngine(loaded);
        }
    }

    public static void SaveAndReload(IList<KeyBinding> bindings, bool mappingEnabled) {
        string path;
        lock (gate) path = configPath;
        KeyMapConfig.WriteFile(path, bindings, mappingEnabled);
        enabled = mappingEnabled;
        Console.WriteLine("[KEYMAP] saved " + bindings.Count + " line(s) -> " + Path.GetFullPath(path));
        Load(path);
    }

    public static List<KeyBinding> Snapshot() {
        lock (gate) return new List<KeyBinding>(loaded);
    }

    public static bool Handle(ushort sourceVk, bool isDown, bool injected, uint eventTime, out MappedKeyEvent[] actions) {
        if (!enabled) {
            actions = new MappedKeyEvent[0];
            return false;
        }
        KeyMapEngine current;
        lock (gate) current = engine;
        return current.HandleTimed(sourceVk, isDown, injected, eventTime, out actions);
    }

    public static MappedKeyEvent[] TakeDueActions(uint currentTime) {
        if (!enabled) return new MappedKeyEvent[0];
        KeyMapEngine current;
        lock (gate) current = engine;
        return current.TakeDueActions(currentTime);
    }
}
