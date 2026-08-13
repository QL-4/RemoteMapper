using System.Collections.Generic;

public sealed class MappedKeyEvent {
    public ushort[] Combo { get; private set; }
    public bool IsDown { get; private set; }
    public bool IsTap { get; private set; }
    public KeyActionKind Action { get; private set; }
    public string Command { get; private set; }

    public MappedKeyEvent(ushort[] combo, bool isDown)
        : this(combo, isDown, false, KeyActionKind.Combo, null) { }

    public MappedKeyEvent(ushort[] combo, bool isDown, bool isTap)
        : this(combo, isDown, isTap, KeyActionKind.Combo, null) { }

    public MappedKeyEvent(ushort[] combo, bool isDown, bool isTap, KeyActionKind action)
        : this(combo, isDown, isTap, action, null) { }

    public MappedKeyEvent(ushort[] combo, bool isDown, bool isTap, KeyActionKind action, string command) {
        Combo = combo;
        IsDown = isDown;
        IsTap = isTap;
        Action = action;
        Command = command;
    }

    public static MappedKeyEvent Tap(ushort[] combo) {
        return new MappedKeyEvent(combo, false, true, KeyActionKind.Combo, null);
    }

    public static MappedKeyEvent SystemAction(KeyActionKind action) {
        return new MappedKeyEvent(null, false, true, action, null);
    }

    public static MappedKeyEvent CommandAction(KeyActionKind action, string command) {
        return new MappedKeyEvent(null, false, true, action, command);
    }
}

public sealed class KeyMapEngine {
    sealed class RuntimeBinding {
        public ushort[] Combo;
        public bool Tap;
        public KeyActionKind ClickAction;
        public string ClickCommand;
        public uint LongPressMs;
        public ushort[] LongCombo;
        public KeyActionKind LongAction;
        public string LongCommand;
        public uint DoubleMs;
        public ushort[] DoubleCombo;
        public KeyActionKind DoubleAction;
        public string DoubleCommand;
        public uint RepeatDelay;
        public uint RepeatInterval;
        public ushort[] RepeatCombo;
        public KeyActionKind RepeatAction;
        public string RepeatCommand;
        public bool HasClick;
        public bool HasLong;
        public bool HasDouble;
        public bool HasRepeat;
    }

    sealed class KeyState {
        public bool IsHeld;
        public uint PressTime;
        public bool Fired;
        public uint NextRepeat;
        public bool WaitingDouble;
        public uint DoubleDeadline;
        public int PressCount;
    }

    static readonly MappedKeyEvent[] NoActions = new MappedKeyEvent[0];
    readonly Dictionary<ushort, RuntimeBinding> bindings = new Dictionary<ushort, RuntimeBinding>();
    readonly Dictionary<ushort, KeyState> states = new Dictionary<ushort, KeyState>();

    public int BindingCount { get { return bindings.Count; } }

    public KeyMapEngine(IEnumerable<KeyBinding> configuredBindings) {
        foreach (KeyBinding b in configuredBindings) {
            bool hasClick = b.HasClick;
            bool hasLong = b.HasLong;
            bool hasDouble = b.HasDouble;
            bool hasRepeat = b.HasRepeat;
            if (!hasClick && !hasLong && !hasDouble && !hasRepeat) continue;
            if (!hasLong && !hasDouble && !hasRepeat && hasClick
                && b.ClickAction == KeyActionKind.Combo && !b.Tap
                && b.Combo != null && b.Combo.Length == 1 && b.Combo[0] == b.SourceVk)
                continue;
            bindings[b.SourceVk] = new RuntimeBinding {
                Combo = b.Combo ?? new ushort[0],
                Tap = b.Tap,
                ClickAction = b.ClickAction,
                ClickCommand = b.ClickCommand,
                LongPressMs = b.LongPressMs,
                LongCombo = b.LongCombo,
                LongAction = b.LongAction,
                LongCommand = b.LongCommand,
                DoubleMs = b.DoubleMs,
                DoubleCombo = b.DoubleCombo,
                DoubleAction = b.DoubleAction,
                DoubleCommand = b.DoubleCommand,
                RepeatDelay = b.RepeatDelay,
                RepeatInterval = b.RepeatInterval,
                RepeatCombo = b.RepeatCombo,
                RepeatAction = b.RepeatAction,
                RepeatCommand = b.RepeatCommand,
                HasClick = hasClick,
                HasLong = hasLong,
                HasDouble = hasDouble,
                HasRepeat = hasRepeat
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

        bool commandClick = binding.HasClick && binding.ClickAction != KeyActionKind.Combo;
        bool delayed = binding.Tap || commandClick || binding.HasLong || binding.HasDouble || binding.HasRepeat;

        KeyState st;
        if (!states.TryGetValue(sourceVk, out st)) {
            st = new KeyState();
            states[sourceVk] = st;
        }

        if (isDown) {
            if (st.WaitingDouble) {
                st.WaitingDouble = false;
                st.IsHeld = true;
                st.PressTime = eventTime;
                st.Fired = false;
                st.NextRepeat = 0;
                st.PressCount = 2;
                return true;
            }
            if (st.IsHeld) return true;

            st.IsHeld = true;
            st.PressTime = eventTime;
            st.Fired = false;
            st.NextRepeat = 0;
            st.PressCount = 1;

            if (!delayed && binding.HasClick)
                actions = new[] { new MappedKeyEvent(binding.Combo, true) };
            return true;
        }

        if (!st.IsHeld) {
            st.WaitingDouble = false;
            st.PressCount = 0;
            return true;
        }

        st.IsHeld = false;

        if (!delayed) {
            if (binding.HasClick)
                actions = new[] { new MappedKeyEvent(binding.Combo, false) };
            return true;
        }

        if (binding.HasLong && !st.Fired) {
            if (unchecked(eventTime - st.PressTime) >= binding.LongPressMs) {
                st.Fired = true;
                st.PressCount = 0;
                actions = new[] { MakeAction(binding.LongAction, binding.LongCombo, binding.LongCommand) };
                return true;
            }
        }

        if (st.Fired) {
            st.NextRepeat = 0;
            st.PressCount = 0;
            return true;
        }

        if (binding.HasDouble && st.PressCount == 1) {
            st.WaitingDouble = true;
            st.DoubleDeadline = unchecked(eventTime + binding.DoubleMs);
            return true;
        }

        if (st.PressCount >= 2 && binding.HasDouble)
            actions = new[] { MakeAction(binding.DoubleAction, binding.DoubleCombo, binding.DoubleCommand) };
        else if (binding.HasClick)
            actions = new[] { MakeClick(binding) };
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
                if (binding.HasRepeat) {
                    if (!st.Fired) {
                        if (unchecked(currentTime - st.PressTime) >= binding.RepeatDelay) {
                            st.Fired = true;
                            st.NextRepeat = unchecked(currentTime + binding.RepeatInterval);
                            actions.Add(MakeAction(binding.RepeatAction, binding.RepeatCombo, binding.RepeatCommand));
                        }
                    } else if (st.NextRepeat > 0 && currentTime >= st.NextRepeat) {
                        st.NextRepeat = unchecked(currentTime + binding.RepeatInterval);
                        actions.Add(MakeAction(binding.RepeatAction, binding.RepeatCombo, binding.RepeatCommand));
                    }
                } else if (binding.HasLong && !st.Fired) {
                    if (unchecked(currentTime - st.PressTime) >= binding.LongPressMs) {
                        st.Fired = true;
                        actions.Add(MakeAction(binding.LongAction, binding.LongCombo, binding.LongCommand));
                    }
                }
            }

            if (st.WaitingDouble && currentTime >= st.DoubleDeadline) {
                st.WaitingDouble = false;
                st.PressCount = 0;
                if (binding.HasClick) actions.Add(MakeClick(binding));
            }
        }
        return actions.ToArray();
    }

    static MappedKeyEvent MakeClick(RuntimeBinding binding) {
        return MakeAction(binding.ClickAction, binding.Combo, binding.ClickCommand);
    }

    static MappedKeyEvent MakeAction(KeyActionKind kind, ushort[] combo, string command) {
        if (kind == KeyActionKind.Launch || kind == KeyActionKind.Cmd || kind == KeyActionKind.Code)
            return MappedKeyEvent.CommandAction(kind, command);
        if (kind == KeyActionKind.TaskView)
            return MappedKeyEvent.SystemAction(kind);
        return MappedKeyEvent.Tap(combo);
    }
}
