using System;
using System.Collections.Generic;
using System.IO;

public static class KeyMapper {
    static KeyMapEngine engine = new KeyMapEngine(new KeyBinding[0]);

    public static void Load(string path) {
        try {
            if (!File.Exists(path)) {
                Console.WriteLine("[KEYMAP] config not found: " + Path.GetFullPath(path));
                return;
            }

            List<KeyBinding> bindings = KeyMapConfig.ParseLines(File.ReadAllLines(path));
            engine = new KeyMapEngine(bindings);
            Console.WriteLine("[KEYMAP] loaded " + engine.BindingCount + " active mapping(s) from " + Path.GetFullPath(path));
        } catch (Exception ex) {
            engine = new KeyMapEngine(new KeyBinding[0]);
            Console.WriteLine("[KEYMAP] disabled: " + ex.Message);
        }
    }

    public static bool Handle(ushort sourceVk, bool isDown, bool injected, uint eventTime, out MappedKeyEvent[] actions) {
        return engine.HandleTimed(sourceVk, isDown, injected, eventTime, out actions);
    }

    public static MappedKeyEvent[] TakeDueActions(uint currentTime) {
        return engine.TakeDueActions(currentTime);
    }
}
