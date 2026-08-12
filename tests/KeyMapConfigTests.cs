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

        var taskBindings = KeyMapConfig.ParseLines(new[] {
            "主页键 = 0x7F -> TAP LALT+TAB | HOLD 800 -> TASKVIEW"
        });
        Equal(KeyActionKind.TaskView, taskBindings[0].LongAction, "home long action parses TASKVIEW");

        var timedEngine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "主页键 = 0x7F -> TAP LALT+TAB | HOLD 800 -> TASKVIEW",
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
        Equal(0, timedActions.Length, "home down has no early action");
        timedActions = timedEngine.TakeDueActions(4800);
        Equal(1, timedActions.Length, "home long fires at threshold");
        Equal(KeyActionKind.TaskView, timedActions[0].Action, "home long emits TASKVIEW");
        Equal(true, timedEngine.HandleTimed(0x7F, false, false, 5000, out timedActions), "home up swallowed");
        Equal(0, timedActions.Length, "home long up does not fire again");

        // ===== DOUBLE click parsing =====
        var dBindings = KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | DOUBLE 300 -> TAP Y"
        });
        Equal((uint)300, dBindings[0].DoubleMs, "parse DOUBLE ms");
        Equal(1, dBindings[0].DoubleCombo.Length, "parse DOUBLE combo length");
        Equal((ushort)0x59, dBindings[0].DoubleCombo[0], "parse DOUBLE combo Y");

        // ===== REPEAT parsing =====
        var rBindings = KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | REPEAT 350 100 -> TAP Y"
        });
        Equal((uint)350, rBindings[0].RepeatDelay, "parse REPEAT delay");
        Equal((uint)100, rBindings[0].RepeatInterval, "parse REPEAT interval");
        Equal((ushort)0x59, rBindings[0].RepeatCombo[0], "parse REPEAT combo Y");
        Equal(true, rBindings[0].LongCombo == null, "parse REPEAT no HOLD");

        // ===== DOUBLE + HOLD can coexist =====
        var dhBindings = KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | DOUBLE 300 -> TAP Y | HOLD 550 -> TAP Z"
        });
        Equal((uint)300, dhBindings[0].DoubleMs, "double+hold DOUBLE ms");
        Equal((uint)550, dhBindings[0].LongPressMs, "double+hold HOLD ms");

        // ===== DOUBLE + REPEAT can coexist =====
        var drBindings = KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | DOUBLE 300 -> TAP Y | REPEAT 350 100 -> TAP Z"
        });
        Equal((uint)300, drBindings[0].DoubleMs, "double+repeat DOUBLE ms");
        Equal((uint)350, drBindings[0].RepeatDelay, "double+repeat REPEAT delay");

        // ===== HOLD and REPEAT are mutually exclusive =====
        try {
            KeyMapConfig.ParseLines(new[] { "测试键 = 0x80 -> TAP X | HOLD 800 -> TAP Y | REPEAT 350 100 -> TAP Z" });
            Console.WriteLine("FAIL mutual exclusivity: expected FormatException");
            failures++;
        } catch (FormatException) {
            // expected
        }

        // ===== Double-click: two quick presses -> double action =====
        var dcEngine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | DOUBLE 300 -> TAP Y"
        }));
        MappedKeyEvent[] dcActions;
        Equal(true, dcEngine.HandleTimed(0x80, true, false, 1000, out dcActions), "dc first down swallowed");
        Equal(0, dcActions.Length, "dc first down no action");
        Equal(true, dcEngine.HandleTimed(0x80, false, false, 1100, out dcActions), "dc first up swallowed");
        Equal(0, dcActions.Length, "dc first up deferred");
        dcActions = dcEngine.TakeDueActions(1200);
        Equal(0, dcActions.Length, "dc no action before timeout");
        // Second press within window
        Equal(true, dcEngine.HandleTimed(0x80, true, false, 1250, out dcActions), "dc second down swallowed");
        Equal(0, dcActions.Length, "dc second down no action");
        Equal(true, dcEngine.HandleTimed(0x80, false, false, 1300, out dcActions), "dc second up swallowed");
        Equal(1, dcActions.Length, "dc emits one action");
        Equal(true, dcActions[0].IsTap, "dc is tap");
        Equal((ushort)0x59, dcActions[0].Combo[0], "dc emits Y (double action)");

        // ===== Single click with double configured: deferred by window =====
        var scEngine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | DOUBLE 300 -> TAP Y"
        }));
        MappedKeyEvent[] scActions;
        Equal(true, scEngine.HandleTimed(0x80, true, false, 1000, out scActions), "sc down swallowed");
        Equal(true, scEngine.HandleTimed(0x80, false, false, 1100, out scActions), "sc up swallowed");
        Equal(0, scActions.Length, "sc up deferred");
        scActions = scEngine.TakeDueActions(1399);
        Equal(0, scActions.Length, "sc not fired before timeout");
        scActions = scEngine.TakeDueActions(1400);
        Equal(1, scActions.Length, "sc fires at timeout");
        Equal((ushort)0x58, scActions[0].Combo[0], "sc emits X (single action)");

        // ===== Repeat: fires at delay, then at interval =====
        var rpEngine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | REPEAT 350 100 -> TAP Y"
        }));
        MappedKeyEvent[] rpActions;
        Equal(true, rpEngine.HandleTimed(0x80, true, false, 1000, out rpActions), "rp down swallowed");
        Equal(0, rpActions.Length, "rp down no action");
        rpActions = rpEngine.TakeDueActions(1349);
        Equal(0, rpActions.Length, "rp not fired before delay");
        rpActions = rpEngine.TakeDueActions(1350);
        Equal(1, rpActions.Length, "rp fires at delay");
        Equal((ushort)0x59, rpActions[0].Combo[0], "rp emits Y");
        rpActions = rpEngine.TakeDueActions(1450);
        Equal(1, rpActions.Length, "rp fires again at interval");
        rpActions = rpEngine.TakeDueActions(1550);
        Equal(1, rpActions.Length, "rp fires again");
        // Release stops repeat
        Equal(true, rpEngine.HandleTimed(0x80, false, false, 1560, out rpActions), "rp up swallowed");
        Equal(0, rpActions.Length, "rp up no action after fired");
        rpActions = rpEngine.TakeDueActions(1650);
        Equal(0, rpActions.Length, "rp no more after release");

        // ===== Repeat: quick release fires single click =====
        var rp2Engine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | REPEAT 350 100 -> TAP Y"
        }));
        MappedKeyEvent[] rp2Actions;
        Equal(true, rp2Engine.HandleTimed(0x80, true, false, 1000, out rp2Actions), "rp2 down swallowed");
        Equal(true, rp2Engine.HandleTimed(0x80, false, false, 1100, out rp2Actions), "rp2 quick up swallowed");
        Equal(1, rp2Actions.Length, "rp2 quick release emits single click");
        Equal((ushort)0x58, rp2Actions[0].Combo[0], "rp2 single click X");

        // ===== Double + long: double takes priority on quick double-press =====
        var dlEngine = new KeyMapEngine(KeyMapConfig.ParseLines(new[] {
            "测试键 = 0x80 -> TAP X | DOUBLE 300 -> TAP Y | HOLD 550 -> TAP Z"
        }));
        MappedKeyEvent[] dlActions;
        Equal(true, dlEngine.HandleTimed(0x80, true, false, 1000, out dlActions), "dl first down");
        Equal(true, dlEngine.HandleTimed(0x80, false, false, 1100, out dlActions), "dl first up deferred");
        Equal(0, dlActions.Length, "dl first up no action");
        Equal(true, dlEngine.HandleTimed(0x80, true, false, 1200, out dlActions), "dl second down");
        Equal(true, dlEngine.HandleTimed(0x80, false, false, 1300, out dlActions), "dl second up");
        Equal(1, dlActions.Length, "dl emits double action");
        Equal((ushort)0x59, dlActions[0].Combo[0], "dl double Y");

        var repositoryBindings = KeyMapConfig.ParseLines(File.ReadAllLines("keymap.txt"));
        var repositoryEngine = new KeyMapEngine(repositoryBindings);
        Equal(6, repositoryEngine.BindingCount, "repository active binding count");
        MappedKeyEvent[] repositoryActions;

        Equal(true, repositoryEngine.HandleTimed(0x7E, true, false, 100, out repositoryActions), "repository F15 short down");
        Equal(true, repositoryEngine.HandleTimed(0x7E, false, false, 500, out repositoryActions), "repository F15 short up");
        Equal((ushort)0xA2, repositoryActions[0].Combo[0], "repository F15 short LCTRL");
        Equal((ushort)0x5A, repositoryActions[0].Combo[1], "repository F15 short Z");
        Equal(true, repositoryEngine.HandleTimed(0x7E, true, false, 1000, out repositoryActions), "repository F15 long down");
        repositoryActions = repositoryEngine.TakeDueActions(1800);
        Equal((ushort)0xA0, repositoryActions[0].Combo[1], "repository F15 long LSHIFT");
        Equal((ushort)0x5A, repositoryActions[0].Combo[2], "repository F15 long Z");
        repositoryEngine.HandleTimed(0x7E, false, false, 1900, out repositoryActions);

        Equal(true, repositoryEngine.HandleTimed(0x82, true, false, 2000, out repositoryActions), "repository F19 short down");
        Equal(true, repositoryEngine.HandleTimed(0x82, false, false, 2500, out repositoryActions), "repository F19 short up");
        Equal((ushort)0xA4, repositoryActions[0].Combo[0], "repository F19 short LALT");
        Equal((ushort)0x09, repositoryActions[0].Combo[1], "repository F19 short TAB");
        Equal(true, repositoryEngine.HandleTimed(0x82, true, false, 3000, out repositoryActions), "repository F19 long down");
        repositoryActions = repositoryEngine.TakeDueActions(3800);
        Equal(KeyActionKind.TaskView, repositoryActions[0].Action, "repository F19 long TASKVIEW");
        repositoryEngine.HandleTimed(0x82, false, false, 3900, out repositoryActions);

        Equal(true, repositoryEngine.HandleTimed(0x7F, true, false, 4000, out repositoryActions), "repository F16 short down");
        Equal(true, repositoryEngine.HandleTimed(0x7F, false, false, 4100, out repositoryActions), "repository F16 short up");
        Equal((ushort)0xA4, repositoryActions[0].Combo[0], "repository F16 short LALT");
        Equal((ushort)0x58, repositoryActions[0].Combo[1], "repository F16 short X");
        Equal(true, repositoryEngine.HandleTimed(0x7F, true, false, 5000, out repositoryActions), "repository F16 long down");
        repositoryActions = repositoryEngine.TakeDueActions(5800);
        Equal((ushort)0xA4, repositoryActions[0].Combo[0], "repository F16 long LALT");
        Equal((ushort)0x73, repositoryActions[0].Combo[1], "repository F16 long F4");
        repositoryEngine.HandleTimed(0x7F, false, false, 5900, out repositoryActions);

        Equal(true, repositoryEngine.Handle(0x7C, true, false, out action), "repository F13 volume up mapped");
        Equal((ushort)0x08, action.Combo[0], "repository F13 BACK");
        repositoryEngine.Handle(0x7C, false, false, out action);
        Equal(true, repositoryEngine.Handle(0x7D, true, false, out action), "repository F14 volume down mapped");
        Equal((ushort)0x2E, action.Combo[0], "repository F14 DELETE");
        repositoryEngine.Handle(0x7D, false, false, out action);

        Equal(false, repositoryEngine.Handle(0x80, true, false, out action), "repository F17 menu disabled");
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
