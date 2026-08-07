using System;
using System.Collections.Generic;
using System.IO;

class KeyMapConfigTests {
    static int failures;

    static void Equal<T>(T expected, T actual, string name) {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
            Console.WriteLine("FAIL " + name + ": expected=" + expected + " actual=" + actual);
            failures++;
        }
    }

    static void Main() {
        var bindings = KeyMapConfig.ParseLines(new[] {
            "返回键 = 0x7E -> LCTRL+Z"
        });

        Equal(1, bindings.Count, "binding count");
        Equal((ushort)0x7E, bindings[0].SourceVk, "source VK");
        Equal(2, bindings[0].Combo.Length, "combo length");
        Equal((ushort)0xA2, bindings[0].Combo[0], "LCTRL");
        Equal((ushort)0x5A, bindings[0].Combo[1], "Z");

        var engine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "方向上 = 0x26 -> UP",
            "返回键 = 0x7E -> LCTRL+Z"
        }));
        MappedKeyEvent action;
        Equal(false, engine.Handle(0x26, true, false, out action), "same-key mapping passes through");
        Equal(true, engine.Handle(0x7E, true, false, out action), "F15 down swallowed");
        Equal(true, action != null && action.IsDown, "F15 down action");
        Equal(true, engine.Handle(0x7E, true, false, out action), "F15 repeat swallowed");
        Equal<MappedKeyEvent>(null, action, "F15 repeat has no duplicate action");
        Equal(true, engine.Handle(0x7E, false, false, out action), "F15 up swallowed");
        Equal(true, action != null && !action.IsDown, "F15 up action");
        Equal(false, engine.Handle(0x7E, true, true, out action), "injected F15 passes through");

        var repositoryBindings = KeyMapConfig.ParseLines(File.ReadAllLines("keymap.txt"));
        var repositoryEngine = new KeyMapEngine(repositoryBindings);
        Equal(4, repositoryEngine.BindingCount, "repository active binding count");
        Equal(true, repositoryEngine.Handle(0x7E, true, false, out action), "repository F15 mapped");
        Equal((ushort)0xA2, action.Combo[0], "repository F15 LCTRL");
        Equal((ushort)0x5A, action.Combo[1], "repository F15 Z");
        Equal(true, repositoryEngine.Handle(0xFF, true, false, out action), "repository power mapped");
        Equal((ushort)0x1B, action.Combo[0], "repository power ESC");

        if (failures != 0) Environment.Exit(1);
        Console.WriteLine("PASS keymap parse and event behavior");
    }
}
