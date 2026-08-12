using System.Collections.Generic;

public sealed class MappedKeyEvent {
    public ushort[] Combo { get; private set; }
    public bool IsDown { get; private set; }
    public bool IsTap { get; private set; }
    public KeyActionKind Action { get; private set; }

    public MappedKeyEvent(ushort[] combo, bool isDown)
        : this(combo, isDown, false, KeyActionKind.Combo) { }

    public MappedKeyEvent(ushort[] combo, bool isDown, bool isTap)
        : this(combo, isDown, isTap, KeyActionKind.Combo) { }

    public MappedKeyEvent(ushort[] combo, bool isDown, bool isTap, KeyActionKind action) {
        Combo = combo;
        IsDown = isDown;
        IsTap = isTap;
        Action = action;
    }

    public static MappedKeyEvent Tap(ushort[] combo) {
        return new MappedKeyEvent(combo, false, true, KeyActionKind.Combo);
    }

    public static MappedKeyEvent SystemAction(KeyActionKind action) {
        return new MappedKeyEvent(null, false, true, action);
    }
}

public sealed class KeyMapEngine {
    sealed class RuntimeBinding {
        public ushort[] Combo;
        public bool Tap;
        public uint LongPressMs;
        public ushort[] LongCombo;
        public KeyActionKind LongAction;
        public uint DoubleMs;
        public ushort[] DoubleCombo;
        public KeyActionKind DoubleAction;
        public uint RepeatDelay;
        public uint RepeatInterval;
        public ushort[] RepeatCombo;
        public KeyActionKind RepeatAction;
    }

    // Per-key runtime state for gesture detection
    sealed class KeyState {
        public bool IsHeld;
        public uint PressTime;
        public bool Fired;          // long-press or first repeat fired
        public uint NextRepeat;     // 0 = no repeat scheduled
        public bool WaitingDouble;  // released, waiting for potential second press
        public uint DoubleDeadline; // when double-click window expires
        public int PressCount;      // 1 or 2
    }

    static readonly MappedKeyEvent[] NoActions = new MappedKeyEvent[0];
    readonly Dictionary<ushort, RuntimeBinding> bindings = new Dictionary<ushort, RuntimeBinding>();
    readonly Dictionary<ushort, KeyState> states = new Dictionary<ushort, KeyState>();

    public int BindingCount { get { return bindings.Count; } }

    public KeyMapEngine(IEnumerable<KeyBinding> configuredBindings) {
        foreach (KeyBinding b in configuredBindings) {
            if (b.Combo == null || b.Combo.Length == 0) continue;
            bool hasGesture = b.LongCombo != null || b.DoubleCombo != null || b.RepeatCombo != null;
            if (!hasGesture && b.Combo.Length == 1 && b.Combo[0] == b.SourceVk) continue;
            bindings[b.SourceVk] = new RuntimeBinding {
                Combo = b.Combo,
                Tap = b.Tap,
                LongPressMs = b.LongPressMs,
                LongCombo = b.LongCombo,
                LongAction = b.LongAction,
                DoubleMs = b.DoubleMs,
                DoubleCombo = b.DoubleCombo,
                DoubleAction = b.DoubleAction,
                RepeatDelay = b.RepeatDelay,
                RepeatInterval = b.RepeatInterval,
                RepeatCombo = b.RepeatCombo,
                RepeatAction = b.RepeatAction
            };
        }
    }

    public bool Handle(ushort sourceVk, bool isDown, bool injected, out MappedKeyEvent action) {
        MappedKeyEvent[] actions;
        bool handled = HandleTimed(sourceVk, isDown, injected, 0, out actions);
        action = actions.Length == 0 ? null : actions[0];
        return handled;
    }

    public bool HandleTimed(ushort sourceVk, bool isDown, bool injected, uint eventTime, out MappedKeyEvent[] actions) {
        actions = NoActions;
        if (injected) return false;

        RuntimeBinding binding;
        if (!bindings.TryGetValue(sourceVk, out binding)) return false;

        bool delayed = binding.Tap || binding.LongCombo != null
            || binding.DoubleCombo != null || binding.RepeatCombo != null;

        KeyState st;
        if (!states.TryGetValue(sourceVk, out st)) {
            st = new KeyState();
            states[sourceVk] = st;
        }

        if (isDown) {
            // ===== KEY DOWN =====
            if (st.WaitingDouble) {
                // Second press within double-click window
                st.WaitingDouble = false;
                st.IsHeld = true;
                st.PressTime = eventTime;
                st.Fired = false;
                st.NextRepeat = 0;
                st.PressCount = 2;
                return true;
            }
            if (st.IsHeld) return true; // auto-repeat down, swallow

            // First press
            st.IsHeld = true;
            st.PressTime = eventTime;
            st.Fired = false;
            st.NextRepeat = 0;
            st.PressCount = 1;

            if (!delayed)
                actions = new[] { new MappedKeyEvent(binding.Combo, true) };
            return true;
        }

        // ===== KEY UP =====
        if (!st.IsHeld) {
            // Stray up (no matching down)
            st.WaitingDouble = false;
            st.PressCount = 0;
            return true;
        }

        st.IsHeld = false;

        if (!delayed) {
            // Immediate-hold: release the combo
            actions = new[] { new MappedKeyEvent(binding.Combo, false) };
            return true;
        }

        // Edge case: released at/after long threshold but before TakeDueActions polled
        if (binding.LongCombo != null && !st.Fired) {
            if (unchecked(eventTime - st.PressTime) >= binding.LongPressMs) {
                st.Fired = true;
                st.PressCount = 0;
                actions = new[] { MakeAction(binding.LongAction, binding.LongCombo) };
                return true;
            }
        }

        if (st.Fired) {
            // Long or repeat already fired; clean up
            st.NextRepeat = 0;
            st.PressCount = 0;
            return true;
        }

        // Released before long/repeat threshold
        if (binding.DoubleCombo != null && st.PressCount == 1) {
            // Start double-click window; single click deferred
            st.WaitingDouble = true;
            st.DoubleDeadline = unchecked(eventTime + binding.DoubleMs);
            return true;
        }

        // Fire click action now (single or double)
        if (st.PressCount >= 2 && binding.DoubleCombo != null)
            actions = new[] { MappedKeyEvent.Tap(binding.DoubleCombo) };
        else
            actions = new[] { MappedKeyEvent.Tap(binding.Combo) };
        st.PressCount = 0;
        st.WaitingDouble = false;
        return true;
    }

    public MappedKeyEvent[] TakeDueActions(uint currentTime) {
        var actions = new List<MappedKeyEvent>();
        foreach (var pair in states) {
            KeyState st = pair.Value;
            RuntimeBinding binding;
            if (!bindings.TryGetValue(pair.Key, out binding)) continue;

            if (st.IsHeld) {
                // Hold-repeat mode (mutually exclusive with long)
                if (binding.RepeatCombo != null) {
                    if (!st.Fired) {
                        if (unchecked(currentTime - st.PressTime) >= binding.RepeatDelay) {
                            st.Fired = true;
                            st.NextRepeat = unchecked(currentTime + binding.RepeatInterval);
                            actions.Add(MakeAction(binding.RepeatAction, binding.RepeatCombo));
                        }
                    } else if (st.NextRepeat > 0 && currentTime >= st.NextRepeat) {
                        st.NextRepeat = unchecked(currentTime + binding.RepeatInterval);
                        actions.Add(MakeAction(binding.RepeatAction, binding.RepeatCombo));
                    }
                }
                // Long-press mode
                else if (binding.LongCombo != null && !st.Fired) {
                    if (unchecked(currentTime - st.PressTime) >= binding.LongPressMs) {
                        st.Fired = true;
                        actions.Add(MakeAction(binding.LongAction, binding.LongCombo));
                    }
                }
            }

            // Double-click timeout: single click fires
            if (st.WaitingDouble && currentTime >= st.DoubleDeadline) {
                st.WaitingDouble = false;
                st.PressCount = 0;
                actions.Add(MappedKeyEvent.Tap(binding.Combo));
            }
        }
        return actions.ToArray();
    }

    static MappedKeyEvent MakeAction(KeyActionKind kind, ushort[] combo) {
        return kind == KeyActionKind.Combo ? MappedKeyEvent.Tap(combo) : MappedKeyEvent.SystemAction(kind);
    }
}
