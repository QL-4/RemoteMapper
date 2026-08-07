using System.Collections.Generic;

public sealed class MappedKeyEvent {
    public ushort[] Combo { get; private set; }
    public bool IsDown { get; private set; }

    public MappedKeyEvent(ushort[] combo, bool isDown) {
        Combo = combo;
        IsDown = isDown;
    }
}

public sealed class KeyMapEngine {
    readonly Dictionary<ushort, ushort[]> bindings = new Dictionary<ushort, ushort[]>();
    readonly HashSet<ushort> held = new HashSet<ushort>();

    public int BindingCount { get { return bindings.Count; } }

    public KeyMapEngine(IEnumerable<KeyBinding> configuredBindings) {
        foreach (KeyBinding binding in configuredBindings) {
            if (binding.Combo == null || binding.Combo.Length == 0) continue;
            if (binding.Combo.Length == 1 && binding.Combo[0] == binding.SourceVk) continue;
            bindings[binding.SourceVk] = binding.Combo;
        }
    }

    public bool Handle(ushort sourceVk, bool isDown, bool injected, out MappedKeyEvent action) {
        action = null;
        if (injected) return false;

        ushort[] combo;
        if (!bindings.TryGetValue(sourceVk, out combo)) return false;

        if (isDown) {
            if (held.Add(sourceVk)) action = new MappedKeyEvent(combo, true);
        } else {
            if (held.Remove(sourceVk)) action = new MappedKeyEvent(combo, false);
        }
        return true;
    }
}
