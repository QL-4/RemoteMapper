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

        var longBindings = KeyMapConfig.ParseLines(new[] {
            "电源键 = 0x82 -> LALT+X | HOLD 800 -> LALT+F4"
        });
        Equal((uint)800, longBindings[0].LongPressMs, "long press threshold");
        Equal(2, longBindings[0].LongCombo.Length, "long combo length");
        Equal((ushort)0xA4, longBindings[0].LongCombo[0], "long combo LALT");
        Equal((ushort)0x73, longBindings[0].LongCombo[1], "long combo F4");

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

        var timedEngine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "主页键 = 0x7F -> TAP LWIN+TAB",
            "电源键 = 0x82 -> TAP LALT+X | HOLD 800 -> TAP LALT+F4"
        }));
        MappedKeyEvent[] timedActions;
        Equal(true, timedEngine.HandleTimed(0x82, true, false, 1000, out timedActions), "power down swallowed");
        Equal(0, timedActions.Length, "power down has no early action");
        Equal(true, timedEngine.HandleTimed(0x82, false, false, 1500, out timedActions), "power short up swallowed");
        Equal(1, timedActions.Length, "power short emits one tap");
        Equal(true, timedActions[0].IsTap, "power short is tap");
        Equal((ushort)0x58, timedActions[0].Combo[1], "power short X");

        Equal(true, timedEngine.HandleTimed(0x82, true, false, 2000, out timedActions), "power long down swallowed");
        timedActions = timedEngine.TakeDueActions(2799);
        Equal(0, timedActions.Length, "power long not early");
        timedActions = timedEngine.TakeDueActions(2800);
        Equal(1, timedActions.Length, "power long fires at threshold");
        Equal(true, timedActions[0].IsTap, "power long is tap");
        Equal((ushort)0x73, timedActions[0].Combo[1], "power long F4");
        Equal(true, timedEngine.HandleTimed(0x82, false, false, 2900, out timedActions), "power long up swallowed");
        Equal(0, timedActions.Length, "power long up does not fire again");
        Equal(true, timedEngine.HandleTimed(0x82, true, false, 3000, out timedActions), "power short after long down");
        Equal(true, timedEngine.HandleTimed(0x82, false, false, 3200, out timedActions), "power short after long up");
        Equal(1, timedActions.Length, "power short after long emits once");
        Equal((ushort)0x58, timedActions[0].Combo[1], "power short after long X");

        Equal(true, timedEngine.HandleTimed(0x7F, true, false, 4000, out timedActions), "home down swallowed");
        Equal(0, timedActions.Length, "home down has no Win injection");
        Equal(true, timedEngine.HandleTimed(0x7F, false, false, 5000, out timedActions), "home up swallowed");
        Equal(1, timedActions.Length, "home emits one tap on release");
        Equal((ushort)0x5B, timedActions[0].Combo[0], "home tap LWIN");
        Equal((ushort)0x09, timedActions[0].Combo[1], "home tap TAB");

        var repositoryBindings = KeyMapConfig.ParseLines(File.ReadAllLines("keymap.txt"));
        var repositoryEngine = new KeyMapEngine(repositoryBindings);
        Equal(5, repositoryEngine.BindingCount, "repository active binding count");
        Equal(true, repositoryEngine.Handle(0x7E, true, false, out action), "repository F15 mapped");
        Equal((ushort)0xA2, action.Combo[0], "repository F15 LCTRL");
        Equal((ushort)0x5A, action.Combo[1], "repository F15 Z");
        repositoryEngine.Handle(0x7E, false, false, out action);

        MappedKeyEvent[] repositoryActions;
        Equal(true, repositoryEngine.HandleTimed(0x82, true, false, 1000, out repositoryActions), "repository F19 short down");
        Equal(0, repositoryActions.Length, "repository F19 waits for release");
        Equal(true, repositoryEngine.HandleTimed(0x82, false, false, 1500, out repositoryActions), "repository F19 short up");
        Equal((ushort)0xA4, repositoryActions[0].Combo[0], "repository F19 short LALT");
        Equal((ushort)0x58, repositoryActions[0].Combo[1], "repository F19 short X");
        Equal(true, repositoryEngine.HandleTimed(0x82, true, false, 2000, out repositoryActions), "repository F19 long down");
        repositoryActions = repositoryEngine.TakeDueActions(2800);
        Equal(1, repositoryActions.Length, "repository F19 fires at threshold");
        Equal((ushort)0x73, repositoryActions[0].Combo[1], "repository F19 long F4");
        Equal(true, repositoryEngine.HandleTimed(0x82, false, false, 2900, out repositoryActions), "repository F19 long up");
        Equal(0, repositoryActions.Length, "repository F19 long up no duplicate");

        Equal(true, repositoryEngine.HandleTimed(0x7F, true, false, 3000, out repositoryActions), "repository F16 home down");
        Equal(0, repositoryActions.Length, "repository F16 waits for release");
        Equal(true, repositoryEngine.HandleTimed(0x7F, false, false, 3100, out repositoryActions), "repository F16 home up");
        Equal((ushort)0x5B, repositoryActions[0].Combo[0], "repository F16 LWIN");
        Equal((ushort)0x09, repositoryActions[0].Combo[1], "repository F16 TAB");

        Equal(true, repositoryEngine.Handle(0x80, true, false, out action), "repository F17 menu mapped");
        repositoryEngine.Handle(0x80, false, false, out action);
        Equal(true, repositoryEngine.Handle(0x81, true, false, out action), "repository F18 live mapped");
        Equal((ushort)0x1B, action.Combo[0], "repository F18 ESC");
        repositoryEngine.Handle(0x81, false, false, out action);

        // Remote-only F-keys avoid collisions with ordinary physical keyboard keys.
        Equal(false, repositoryEngine.Handle(0x24, true, false, out action), "physical Home passes through");
        Equal(false, repositoryEngine.Handle(0x5D, true, false, out action), "physical Apps passes through");
        Equal(false, repositoryEngine.Handle(0xC0, true, false, out action), "physical backtick passes through");
        Equal(false, repositoryEngine.Handle(0xFF, true, false, out action), "physical Power passes through");

        if (failures != 0) Environment.Exit(1);
        Console.WriteLine("PASS keymap parse and event behavior");
    }
}
