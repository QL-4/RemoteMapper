using System.Collections.Generic;

public sealed class MappedKeyEvent {
    public ushort[] Combo { get; private set; }
    public bool IsDown { get; private set; }
    public bool IsTap { get; private set; }

    public MappedKeyEvent(ushort[] combo, bool isDown)
        : this(combo, isDown, false) { }

    public MappedKeyEvent(ushort[] combo, bool isDown, bool isTap) {
        Combo = combo;
        IsDown = isDown;
        IsTap = isTap;
    }

    public static MappedKeyEvent Tap(ushort[] combo) {
        return new MappedKeyEvent(combo, false, true);
    }
}

public sealed class KeyMapEngine {
    sealed class RuntimeBinding {
        public ushort[] Combo;
        public bool Tap;
        public uint LongPressMs;
        public ushort[] LongCombo;
    }

    static readonly MappedKeyEvent[] NoActions = new MappedKeyEvent[0];
    readonly Dictionary<ushort, RuntimeBinding> bindings = new Dictionary<ushort, RuntimeBinding>();
    readonly HashSet<ushort> held = new HashSet<ushort>();
    readonly Dictionary<ushort, uint> downTimes = new Dictionary<ushort, uint>();
    readonly HashSet<ushort> longFired = new HashSet<ushort>();

    public int BindingCount { get { return bindings.Count; } }

    public KeyMapEngine(IEnumerable<KeyBinding> configuredBindings) {
        foreach (KeyBinding binding in configuredBindings) {
            if (binding.Combo == null || binding.Combo.Length == 0) continue;
            if (binding.LongCombo == null && binding.Combo.Length == 1 && binding.Combo[0] == binding.SourceVk) continue;
            bindings[binding.SourceVk] = new RuntimeBinding {
                Combo = binding.Combo,
                Tap = binding.Tap,
                LongPressMs = binding.LongPressMs,
                LongCombo = binding.LongCombo
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

        bool delayed = binding.Tap || binding.LongCombo != null;
        if (isDown) {
            if (!held.Add(sourceVk)) return true;
            downTimes[sourceVk] = eventTime;
            longFired.Remove(sourceVk);
            if (!delayed) actions = new[] { new MappedKeyEvent(binding.Combo, true) };
            return true;
        }

        uint downTime;
        bool wasHeld = held.Remove(sourceVk);
        bool hadTime = downTimes.TryGetValue(sourceVk, out downTime);
        downTimes.Remove(sourceVk);
        bool firedLong = longFired.Remove(sourceVk);
        if (!wasHeld) return true;

        if (!delayed) {
            actions = new[] { new MappedKeyEvent(binding.Combo, false) };
            return true;
        }

        if (firedLong) return true;

        ushort[] combo = binding.Combo;
        if (binding.LongCombo != null && hadTime) {
            uint elapsed = unchecked(eventTime - downTime);
            if (elapsed >= binding.LongPressMs) combo = binding.LongCombo;
        }
        actions = new[] { MappedKeyEvent.Tap(combo) };
        return true;
    }

    public MappedKeyEvent[] TakeDueActions(uint currentTime) {
        var actions = new List<MappedKeyEvent>();
        foreach (KeyValuePair<ushort, uint> entry in downTimes) {
            RuntimeBinding binding;
            if (!held.Contains(entry.Key) || longFired.Contains(entry.Key) ||
                !bindings.TryGetValue(entry.Key, out binding) || binding.LongCombo == null) continue;

            uint elapsed = unchecked(currentTime - entry.Value);
            if (elapsed < binding.LongPressMs) continue;

            longFired.Add(entry.Key);
            actions.Add(MappedKeyEvent.Tap(binding.LongCombo));
        }
        return actions.ToArray();
    }
}
