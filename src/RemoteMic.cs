// RemoteMic.cs - real-time pipeline: BLE ATVV -> decode -> VB-Cable + hotkey linkage
// Hold voice button on remote -> streams decoded mic audio to CABLE + holds [RAlt+Comma] for WeChat IME
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

class RemoteMic {
    // ===== ATVV service/characteristics =====
    static readonly Guid SVC   = Guid.Parse("ab5e0001-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CMD = Guid.Parse("ab5e0002-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_AUD = Guid.Parse("ab5e0003-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CTL = Guid.Parse("ab5e0004-5a21-4f05-bc7d-af01f617b664");

    // ===== audio params (locked in Phase 2) =====
    const int SR = 16000;
    const int FRAME = 120;        // bytes per BLE audio frame
    const int FRAME_SAMPLES = 240; // 120 bytes * 2 nibbles
    static readonly int[] STEP = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] IDX = { -1,-1,-1,-1,2,4,6,8 };
    const double GAIN = 8.0;  // legacy fixed gain (unused when AGC on)
    // AGC state
    static double agcPeak = 1000;
    const double AGC_TARGET = 28000;
    const double AGC_DECAY = 0.9997;   // halves ~144ms (slow release)
    const double AGC_MAX_GAIN = 30;
    const double AGC_FLOOR = 200;

    // ===== state machine =====
    enum State { Idle, Talking }
    static volatile State state = State.Idle;
    static int predictor = 0, stepIndex = 0;
    static short lastSample = 0;     // for cross-frame lowpass continuity
    static short prevDecoded = 0;    // for cross-frame declip continuity

    // ===== key injection worker thread (avoids doing SendInput inside hook/WinRT callbacks) =====
    sealed class KeyAction {
        public const int VoiceHold = 1, VoiceRelease = 2, MapDown = 3, MapUp = 4, MapTap = 5, TaskView = 6;
        public int Kind;
        public ushort[] Combo;
        public KeyAction(int kind, ushort[] combo) { Kind = kind; Combo = combo; }
    }
    static System.Collections.Concurrent.BlockingCollection<KeyAction> keyQueue =
        new System.Collections.Concurrent.BlockingCollection<KeyAction>();
    static Thread keyThread;
    static void StartKeyWorker() {
        keyThread = new Thread(() => {
            foreach (var act in keyQueue.GetConsumingEnumerable()) {
                try {
                    if (act.Kind == KeyAction.VoiceHold) {
                        DeviceSwitch.SwitchToCable();
                        KeySim.HoldCombo();
                    } else if (act.Kind == KeyAction.VoiceRelease) {
                        KeySim.ReleaseCombo();
                        DeviceSwitch.Restore();
                    } else if (act.Kind == KeyAction.MapDown) {
                        KeySim.PressMappedCombo(act.Combo);
                    } else if (act.Kind == KeyAction.MapUp) {
                        KeySim.ReleaseMappedCombo(act.Combo);
                    } else if (act.Kind == KeyAction.MapTap) {
                        KeySim.TapMappedCombo(act.Combo);
                    } else if (act.Kind == KeyAction.TaskView) {
                        KeySim.OpenTaskView();
                    }
                } catch (Exception ex) { Console.WriteLine("[KEY] worker err: " + ex.Message); }
            }
        }) { IsBackground = true, Name = "keyworker" };
        keyThread.Start();
    }

    public static void QueueMappedKey(MappedKeyEvent action) {
        int kind = action.Action == KeyActionKind.TaskView ? KeyAction.TaskView :
            (action.IsTap ? KeyAction.MapTap : (action.IsDown ? KeyAction.MapDown : KeyAction.MapUp));
        keyQueue.Add(new KeyAction(kind, action.Combo));
    }

    // ===== playback =====
    static WaveStreamer streamer;
    static long totalFramesPlayed = 0;
    static DateTime sessionStart;
    static System.Collections.Generic.List<short> dumpSamples; // for REMOTEMIC_DUMP

    // ===== BLE =====
    static BluetoothLEDevice device;
    static GattCharacteristic chCmd;
    static bool hotkeyEnabled = Environment.GetEnvironmentVariable("REMOTEMIC_HOTKEY") != "0";
    static bool dumpEnabled = Environment.GetEnvironmentVariable("REMOTEMIC_DUMP") == "1";

    static async Task Run() {
        Console.Title = "RemoteMic - Xiaomi Remote -> VB-Cable";
        Console.WriteLine("== RemoteMic: remote mic -> CABLE + WeChat IME hotkey ==");

        // 1. connect BLE  (match by device name "MI RC" so it works across remotes/machines;
        //                   fall back to a known MAC prefix)
        Console.Write("[1/4] connecting to remote...");
        var sel = BluetoothLEDevice.GetDeviceSelector();
        var devs = await AsT(DeviceInformation.FindAllAsync(sel));
        var di = devs.FirstOrDefault(d => d.Name.IndexOf("MI RC", StringComparison.OrdinalIgnoreCase) >= 0)
               ?? devs.FirstOrDefault(d => d.Id.IndexOf("c0:5d:39", StringComparison.OrdinalIgnoreCase) >= 0);
        if (di == null) { Console.WriteLine(" NOT FOUND (turn on remote, re-pair if needed)"); return; }
        device = await AsT(BluetoothLEDevice.FromIdAsync(di.Id));
        if (device == null) { Console.WriteLine(" FromIdAsync failed"); return; }
        Console.WriteLine(" OK (" + di.Name + ")");

        // 2. GATT setup (retry: service enumeration can be empty if BLE not ready)
        Console.Write("[2/4] setting up ATVV service...");
        GattDeviceServicesResult svcRes = null;
        for (int attempt = 0; attempt < 5; attempt++) {
            svcRes = await AsT(device.GetGattServicesAsync(BluetoothCacheMode.Uncached));
            if (svcRes.Services.Any(s => s.Uuid == SVC)) break;
            await Task.Delay(1000);
        }
        var svc = svcRes.Services.First(s => s.Uuid == SVC);
        var chRes = await AsT(svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached));
        chCmd = chRes.Characteristics.First(c => c.Uuid == C_CMD);
        var chAud = chRes.Characteristics.First(c => c.Uuid == C_AUD);
        var chCtl = chRes.Characteristics.First(c => c.Uuid == C_CTL);
        HookEvent(chCtl, MakeCtlHandler());
        HookEvent(chAud, MakeAudioHandler());
        await AsT(chCtl.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify));
        await AsT(chAud.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify));
        Console.WriteLine(" OK");

        // 3. open CABLE
        Console.Write("[3/4] opening VB-Cable Input...");
        streamer = new WaveStreamer();
        if (!streamer.Start("CABLE Input", SR)) { Console.WriteLine(" CABLE not found!"); return; }
        Console.WriteLine(" OK");
        StartKeyWorker();
        KeyMapper.Load("keymap.txt");
        F5Blocker.Start();
        if (DeviceSwitch.FindCable())
            Console.WriteLine("[DEV] CABLE Output found as device; will auto-switch default capture while talking");
        else
            Console.WriteLine("[DEV] CABLE Output not found — set system default capture to CABLE Output manually");

        // 4. ATVV handshake
        Console.Write("[4/4] ATVV handshake...");
        await WriteCmd(new byte[] { 0x0A, 0x01, 0x00, 0x00, 0x03, 0x03 }); // GET_CAPS
        await Task.Delay(600);
        await WriteCmd(new byte[] { 0x0C, 0x00 }); // MIC_OPEN
        await Task.Delay(500);
        Console.WriteLine(" ready");

        Console.WriteLine("===========================================================");
        Console.WriteLine(">> HOLD the voice button to talk. Release to stop.");
        Console.WriteLine(">> WeChat IME hotkey [RAlt + Comma] will be held for you.");
        Console.WriteLine(">> Make sure WeChat IME's mic = system default = CABLE Output,");
        Console.WriteLine("   OR per-app mic set to CABLE Output for wetype_server.exe.");
        Console.WriteLine(">> Ctrl+C to exit.");
        Console.WriteLine("===========================================================\n");

        // keepalive loop
        while (true) {
            await Task.Delay(5000);
            try { await WriteCmd(new byte[] { 0x0E, 0x00 }); } catch { } // MIC_EXTEND
        }
    }

    static TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> MakeCtlHandler() {
        return (s, e) => {
            var b = ToB(e.CharacteristicValue);
            if (b.Length < 1) return;
            byte op = b[0];
            // AUDIO_START with HTT reason: byte1 == 0x03
            if (op == 0x04 && b.Length >= 2 && b[1] == 0x03) {
                // Voice button PRESSED
                Console.WriteLine("\n[VOICE] >>> button PRESSED (HTT) -> hotkey DOWN, start streaming");
                predictor = 0; stepIndex = 0; lastSample = 0; prevDecoded = 0; agcPeak = 1000;
                totalFramesPlayed = 0; sessionStart = DateTime.Now;
                if (dumpEnabled) dumpSamples = new System.Collections.Generic.List<short>();
                state = State.Talking;
                if (hotkeyEnabled) keyQueue.Add(new KeyAction(KeyAction.VoiceHold, null)); else Console.WriteLine("[VOICE]    (hotkey DISABLED - audio only test)");
            }
            // AUDIO_STOP with HTT-release: byte1 == 0x02
            else if (op == 0x00 && b.Length >= 2 && b[1] == 0x02) {
                Console.WriteLine("[VOICE] <<< button RELEASED (HTT) -> hotkey UP, stop streaming" +
                    (state == State.Talking ? "  (" + totalFramesPlayed + " frames, " + (DateTime.Now - sessionStart).TotalSeconds.ToString("0.0") + "s)" : ""));
                state = State.Idle;
                if (dumpEnabled && dumpSamples != null && dumpSamples.Count > 0) {
                    string path = @"D:\Projects\RemoteMapper\rt_dump_" + DateTime.Now.ToString("HHmmss") + ".wav";
                    WriteWav(path, dumpSamples.ToArray(), SR);
                    Console.WriteLine("[DUMP] saved " + dumpSamples.Count + " samples -> " + path);
                    dumpSamples = null;
                }
                if (hotkeyEnabled) keyQueue.Add(new KeyAction(KeyAction.VoiceRelease, null));
            }
            else if (op == 0x00 && b.Length >= 2 && b[1] == 0x00) {
                Console.WriteLine("[VOICE] MIC_CLOSED");
                state = State.Idle;
            }
            else if (op == 0x0B) {
                // CAPS_RESP
            }
        };
    }

    static TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> MakeAudioHandler() {
        return (s, e) => {
            if (state != State.Talking) return;
            var b = ToB(e.CharacteristicValue);
            if (b.Length != FRAME) {
                // unexpected; decode what we have
            }
            var samples = new short[FRAME_SAMPLES];
            int pred = predictor, si = stepIndex;
            int n = Math.Min(b.Length, FRAME);
            int k = 0;
            for (int i = 0; i < n; i++) {
                samples[k++] = (short)Nibble(b[i] >> 4, ref pred, ref si);
                samples[k++] = (short)Nibble(b[i] & 0xF, ref pred, ref si);
            }
            predictor = pred; stepIndex = si;
            // post-process: declip (within frame), lowpass (cross-frame), AGC
            Declip(samples);
            Lowpass(samples);
            for (int i = 0; i < samples.Length; i++) {
                double v = samples[i];
                double a = v < 0 ? -v : v;
                if (a > agcPeak) agcPeak = a; else agcPeak *= AGC_DECAY;
                double g = Math.Min(AGC_MAX_GAIN, AGC_TARGET / Math.Max(agcPeak, AGC_FLOOR));
                v *= g;
                // soft clip (tanh-like)
                if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                samples[i] = (short)v;
            }
            streamer.Enqueue(samples);
            if (dumpEnabled) dumpSamples.AddRange(samples);
            Interlocked.Increment(ref totalFramesPlayed);
        };
    }

    static int Nibble(int nibble, ref int predictor, ref int stepIndex) {
        int step = STEP[stepIndex];
        int diff = step >> 3;
        if ((nibble & 1) != 0) diff += step >> 2;
        if ((nibble & 2) != 0) diff += step >> 1;
        if ((nibble & 4) != 0) diff += step;
        if ((nibble & 8) != 0) predictor -= diff; else predictor += diff;
        if (predictor > 32767) predictor = 32767;
        if (predictor < -32768) predictor = -32768;
        stepIndex += IDX[nibble & 7];
        if (stepIndex < 0) stepIndex = 0;
        if (stepIndex > 88) stepIndex = 88;
        return predictor;
    }

    static void Declip(short[] s) {
        const int TH = 1000;
        int len = s.Length;
        int prev = prevDecoded;
        for (int i = 0; i < len; i++) {
            int p = i == 0 ? prev : s[i - 1];
            int nx = i == len - 1 ? s[i] : s[i + 1];
            int cur = s[i];
            int dp = Math.Abs(cur - p), dn = Math.Abs(cur - nx);
            int nd = Math.Abs(nx - p);
            if (dp > TH && dn > TH && Math.Min(dp, dn) > nd * 2)
                s[i] = (short)((p + nx) / 2);
        }
        prevDecoded = s[len - 1];
    }
    static void Lowpass(short[] s) {
        if (s.Length == 0) return;
        short prev = lastSample;
        for (int i = 0; i < s.Length - 1; i++) {
            short cur = s[i];
            s[i] = (short)((prev + 2 * cur + s[i + 1]) >> 2);
            prev = cur;
        }
        lastSample = s[s.Length - 1];
    }

    // ===== BLE helpers =====
    static async Task WriteCmd(byte[] data) {
        var w = new DataWriter(); w.WriteBytes(data);
        await AsT(chCmd.WriteValueAsync(w.DetachBuffer()));
    }
    static void HookEvent(object instance, Delegate handler) {
        var mi = instance.GetType().GetMethod("add_ValueChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        mi.Invoke(instance, new object[] { handler });
    }
    static Task<T> AsT<T>(IAsyncOperation<T> op) {
        var tcs = new TaskCompletionSource<T>();
        op.Completed = (o, s) => {
            try {
                if (s == AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                else if (s == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode);
                else tcs.TrySetCanceled();
            } catch (Exception ex) { tcs.TrySetException(ex); }
        };
        return tcs.Task;
    }
    static byte[] ToB(IBuffer buf) { var r = DataReader.FromBuffer(buf); var b = new byte[buf.Length]; r.ReadBytes(b); return b; }
    static void WriteWav(string path, short[] pcm, int sr) {
        int ds = pcm.Length * 2;
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
        using (var w = new System.IO.BinaryWriter(fs)) {
            var a = System.Text.Encoding.ASCII;
            w.Write(a.GetBytes("RIFF")); w.Write(36 + ds); w.Write(a.GetBytes("WAVE"));
            w.Write(a.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(a.GetBytes("data")); w.Write(ds);
            byte[] b = new byte[ds]; System.Buffer.BlockCopy(pcm, 0, b, 0, ds); w.Write(b);
        }
    }

    static void Main() {
        // tee all console output to RemoteMic.log for diagnosing double-click launches
        try {
            var logfile = new System.IO.StreamWriter("RemoteMic.log", false) { AutoFlush = true };
            Console.SetOut(new TeeWriter(Console.Out, logfile));
            Console.WriteLine("=== RemoteMic log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            Console.WriteLine("CWD: " + System.IO.Directory.GetCurrentDirectory());
            Console.WriteLine("SessionId: " + System.Diagnostics.Process.GetCurrentProcess().SessionId);
            Console.WriteLine("Interactive: " + System.Environment.UserInteractive);
        } catch (Exception ex) { Console.WriteLine("[LOG] init failed: " + ex.Message); }

        // graceful Ctrl+C
        Console.CancelKeyPress += (o, ea) => {
            ea.Cancel = true;
            Console.WriteLine("\nExiting...");
            try { KeySim.ReleaseCombo(); } catch { }
            if (streamer != null) streamer.Stop();
            try { DeviceSwitch.Restore(); } catch { }
            try { F5Blocker.Stop(); } catch { }
            Environment.Exit(0);
        };
        try { Run().GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.WriteLine("FATAL: " + ex); Console.ReadLine(); }
    }
}

// ===== TeeWriter: write to console + file simultaneously =====
class TeeWriter : System.IO.TextWriter {
    System.IO.TextWriter _c; System.IO.StreamWriter _f;
    public TeeWriter(System.IO.TextWriter c, System.IO.StreamWriter f) { _c = c; _f = f; }
    public override System.Text.Encoding Encoding { get { return _c.Encoding; } }
    public override void Write(string v) { _c.Write(v); try { _f.Write(v); _f.Flush(); } catch { } }
    public override void WriteLine(string v) { _c.WriteLine(v); try { _f.WriteLine(v); _f.Flush(); } catch { } }
    public override void Write(char v) { _c.Write(v); try { _f.Write(v); _f.Flush(); } catch { } }
}

// ===== F5 blocker: low-level keyboard hook swallows remote's F5 HID spam =====
// Remote voice button emits F5 via HID which pollutes the injected hotkey combo.
// We swallow ALL F5 events while running. Trigger still works via BLE CTL channel.
class F5Blocker {
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint interval, IntPtr callback);
    [DllImport("kernel32.dll")] static extern uint GetTickCount();
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int px, py; }
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint LLKHF_INJECTED = 0x10, LLKHF_LOWER_IL_INJECTED = 0x02;
    const ushort VK_F5 = 0x74;
    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr extra; }
    static HookProc proc = HookCb;
    static volatile IntPtr hhk = IntPtr.Zero;
    static Thread pump;
    static uint pumpTid;
    const uint WM_TIMER = 0x0113, WM_APP_REHOOK = 0x8000;
    static UIntPtr keymapTimerId = UIntPtr.Zero;
    static volatile int f5Count = 0;
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);

    static IntPtr HookCb(int nCode, IntPtr wParam, IntPtr lParam) {
        try {
            if (nCode >= 0) {
                var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                if (k.vkCode == VK_F5) { f5Count++; return (IntPtr)1; }   // swallow voice-button spam

                int message = wParam.ToInt32();
                bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
                if (down || up) {
                    bool injected = (k.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;
                    MappedKeyEvent[] actions;
                    if (KeyMapper.Handle((ushort)k.vkCode, down, injected, k.time, out actions)) {
                        foreach (MappedKeyEvent action in actions) RemoteMic.QueueMappedKey(action);
                        return (IntPtr)1;
                    }
                }
            }
        } catch (Exception ex) { Console.WriteLine("[KEYMAP] hook err: " + ex.Message); }
        return CallNextHookEx(hhk, nCode, wParam, lParam);
    }

    public static void Start() {
        pump = new Thread(() => {
            pumpTid = GetCurrentThreadId();
            try {
                hhk = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                keymapTimerId = SetTimer(IntPtr.Zero, (UIntPtr)1, 25, IntPtr.Zero);
                if (keymapTimerId == UIntPtr.Zero) Console.WriteLine("[KEYMAP] timer install failed");
                Console.WriteLine("[F5] blocker installed");
                MSG m;
                while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) {
                    // re-install hook request from keyworker thread (Resume)
                    if (m.message == WM_APP_REHOOK && hhk == IntPtr.Zero)
                        hhk = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                    if (m.message == WM_TIMER && unchecked((ulong)m.wParam.ToInt64()) == keymapTimerId.ToUInt64()) {
                        foreach (MappedKeyEvent action in KeyMapper.TakeDueActions(GetTickCount()))
                            RemoteMic.QueueMappedKey(action);
                    }
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
            } catch (Exception ex) { Console.WriteLine("[F5] pump err: " + ex.Message); }
        }) { IsBackground = true, Name = "f5pump" };
        pump.Start();
    }
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
    public static void Stop() {
        if (hhk != IntPtr.Zero) { UnhookWindowsHookEx(hhk); hhk = IntPtr.Zero; }
    }
    // Called from keyworker: remove hook so it cannot marshal/disrupt our
    // injected keys. UnhookWindowsHookEx is safe from any thread.
    public static void Suspend() {
        if (hhk != IntPtr.Zero) { UnhookWindowsHookEx(hhk); hhk = IntPtr.Zero; }
    }
    // Called from keyworker: ask the pump thread (which owns the message loop)
    // to re-install the hook. SetWindowsHookEx must run on the thread that pumps.
    public static void Resume() {
        PostThreadMessage(pumpTid, WM_APP_REHOOK, IntPtr.Zero, IntPtr.Zero);
    }
}

// ===== DeviceSwitch: auto-switch default capture device to CABLE Output while talking =====
// On HTT press: save current default -> set CABLE as default. On HTT release: restore.
// Only triggered by remote (ACT_HOLD/RELEASE come from HTT). COM calls run on an STA thread.
class DeviceSwitch {
    enum DSRole : uint { eConsole=0, eMultimedia=1, eCommunications=2 }
    enum DSFlow { eRender=0, eCapture=1, eAll=2 }
    [Flags] enum DSState : uint { ACTIVE=1, DISABLED=2, NOTPRESENT=4, UNPLUGGED=8 }

    [StructLayout(LayoutKind.Sequential)] struct DSPK { public Guid fmtid; public uint pid; }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface DSEnum {
        void EnumAudioEndpoints(DSFlow f, DSState s, out DSColl c);
        void GetDefaultAudioEndpoint(DSFlow f, DSRole r, out DSDev d);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out DSDev d);
        void R1(IntPtr p); void R2(IntPtr p);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface DSColl { void GetCount(out uint n); void Item(uint i, out DSDev d); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface DSDev {
        int Activate();
        void OpenPropertyStore(uint stgm, out DSPropStore ps);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out DSState st);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface DSPropStore {
        void GetCount(out uint n);
        void GetAt(uint i, out DSPK k);
        void GetValue([In] ref DSPK k, IntPtr pv);
        int SetValue();
        void Commit();
    }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] internal class CPolicyConfigClient { }
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface DSPolicy {
        int M0();int M1();int M2();int M3();int M4();int M5();int M6();int M7();int M8();int M9();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, DSRole r);
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(IntPtr pv);
    static readonly Guid PKEY_NAME = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
    static readonly Guid CLSID_ENUM = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

    static string cableId = null;
    static string savedDefault = null;
    public static string CableId { get { return cableId; } }

    static string ReadName(DSPropStore ps) {
        var key = new DSPK { fmtid = PKEY_NAME, pid = 2 };
        IntPtr pv = Marshal.AllocCoTaskMem(40);
        for (int i = 0; i < 40; i++) Marshal.WriteByte(pv, i, 0);
        try {
            ps.GetValue(ref key, pv);
            short vt = Marshal.ReadInt16(pv);
            if (vt == 31) return Marshal.PtrToStringUni(Marshal.ReadIntPtr(pv, 8));
            return null;
        } finally { PropVariantClear(pv); Marshal.FreeCoTaskMem(pv); }
    }
    static string CurrentDefault() {
        var e = (DSEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ENUM));
        DSDev d; e.GetDefaultAudioEndpoint(DSFlow.eCapture, DSRole.eConsole, out d);
        string id; d.GetId(out id); return id;
    }
    static void DoSetDefault(string id) {
        var p = (DSPolicy)new CPolicyConfigClient();
        p.SetDefaultEndpoint(id, DSRole.eConsole);
        p.SetDefaultEndpoint(id, DSRole.eMultimedia);
        p.SetDefaultEndpoint(id, DSRole.eCommunications);
    }
    static void OnSta(Action a) {
        Exception ex = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { ex = e; } }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (ex != null) throw ex;
    }

    // call at startup; returns true if CABLE Output found
    public static bool FindCable() {
        try {
            string found = null;
            OnSta(() => {
                var e = (DSEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ENUM));
                DSColl c; e.EnumAudioEndpoints(DSFlow.eCapture, DSState.ACTIVE, out c);
                uint n; c.GetCount(out n);
                for (uint i = 0; i < n; i++) {
                    DSDev d; c.Item(i, out d);
                    string id; d.GetId(out id);
                    DSPropStore ps; d.OpenPropertyStore(0, out ps);
                    string nm = ReadName(ps);
                    if (nm != null && nm.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0
                        && nm.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0) found = id;
                }
            });
            cableId = found;
            return found != null;
        } catch (Exception ex) { Console.WriteLine("[DEV] FindCable err: " + ex.Message); return false; }
    }

    // call on HTT press (ACT_HOLD) — before injecting hotkey
    public static void SwitchToCable() {
        if (cableId == null) return;
        try {
            OnSta(() => { savedDefault = CurrentDefault(); DoSetDefault(cableId); });
            Console.WriteLine("[DEV] default capture -> CABLE");
        } catch (Exception ex) { Console.WriteLine("[DEV] switch err: " + ex.Message); }
    }

    // call on HTT release (ACT_RELEASE) — after releasing hotkey
    public static void Restore() {
        if (savedDefault == null) return;
        string prev = savedDefault;
        try {
            OnSta(() => DoSetDefault(prev));
            Console.WriteLine("[DEV] default capture restored");
            savedDefault = null;
        } catch (Exception ex) { Console.WriteLine("[DEV] restore err: " + ex.Message); }
    }
}

// ===== waveOut streamer to VB-Cable =====
class WaveStreamer {
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int waveOutGetDevCaps(uint id, ref WAVEOUTCAPS pwoc, int cbwoc);
    [DllImport("winmm.dll")]
    static extern int waveOutOpen(out IntPtr phwi, uint id, ref WAVEFORMATEX pwfx, IntPtr cb, IntPtr inst, uint fdo);
    [DllImport("winmm.dll")]
    static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")]
    static extern int waveOutClose(IntPtr hwo);
    [DllImport("winmm.dll")]
    static extern uint waveOutGetNumDevs();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WAVEOUTCAPS { public ushort wMid, wPid; public uint v; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name; public uint df; public ushort ch, r1; public uint sup; }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX { public ushort tag; public ushort ch; public uint sr; public uint avg; public ushort blk; public ushort bits; public ushort cb; }

    const int NB = 8;
    const int BUF_BYTES = 240 * 2;  // one audio frame
    const int HDR_SIZE = 48;        // x64 WAVEHDR size
    const int OFF_FLAGS = 24;
    const int WHDR_DONE = 1;

    IntPtr hWave = IntPtr.Zero;
    IntPtr[] hdr = new IntPtr[NB];
    IntPtr[] data = new IntPtr[NB];
    bool[] used = new bool[NB];
    Queue<short[]> q = new Queue<short[]>();
    object qlock = new object();
    volatile bool running;
    Thread thr;

    public bool Start(string nameContains, int sr) {
        // find device
        uint n = waveOutGetNumDevs(); uint idx = 0xFFFFFFFF; bool found = false;
        for (uint i = 0; i < n; i++) {
            var c = new WAVEOUTCAPS();
            waveOutGetDevCaps(i, ref c, Marshal.SizeOf(c));
            Console.WriteLine("    out["+i+"] = "+c.name);
            if (c.name.Contains(nameContains)) { idx = i; found = true; }
        }
        if (!found) return false;

        var wfx = new WAVEFORMATEX { tag = 1, ch = 1, sr = (uint)sr, avg = (uint)(sr * 2), blk = 2, bits = 16, cb = 0 };
        int hr = waveOutOpen(out hWave, idx, ref wfx, IntPtr.Zero, IntPtr.Zero, 0);
        if (hr != 0) return false;

        for (int i = 0; i < NB; i++) {
            data[i] = Marshal.AllocHGlobal(BUF_BYTES);
            hdr[i] = Marshal.AllocHGlobal(HDR_SIZE);
            // zero the header
            for (int o = 0; o < HDR_SIZE; o++) Marshal.WriteByte(hdr[i], o, 0);
            Marshal.WriteIntPtr(hdr[i], 0, data[i]);          // lpData
            Marshal.WriteInt32(hdr[i], 8, BUF_BYTES);          // dwBufferLength
            waveOutPrepareHeader(hWave, hdr[i], HDR_SIZE);
        }
        running = true;
        thr = new Thread(Pump) { IsBackground = true, Name = "wavout" };
        thr.Start();
        return true;
    }

    public void Enqueue(short[] samples) {
        lock (qlock) {
            q.Enqueue(samples);
            if (q.Count > 40) { q.Dequeue(); } // drop old if backed up (>600ms)
        }
    }

    public void Stop() {
        running = false;
        if (thr != null) thr.Join(500);
        if (hWave != IntPtr.Zero) {
            waveOutReset(hWave);
            for (int i = 0; i < NB; i++) {
                if (hdr[i] != IntPtr.Zero) { Marshal.FreeHGlobal(hdr[i]); Marshal.FreeHGlobal(data[i]); }
            }
            waveOutClose(hWave);
            hWave = IntPtr.Zero;
        }
    }

    void Pump() {
        while (running) {
            int freeIdx = -1;
            for (int i = 0; i < NB; i++) {
                if (used[i] && (Marshal.ReadInt32(hdr[i], OFF_FLAGS) & WHDR_DONE) != 0)
                    used[i] = false;
                if (!used[i]) { freeIdx = i; break; }
            }
            short[] samples = null;
            if (freeIdx >= 0) {
                lock (qlock) { if (q.Count > 0) samples = q.Dequeue(); }
            }
            if (freeIdx >= 0 && samples != null) {
                int n = Math.Min(samples.Length, 240);
                Marshal.Copy(samples, 0, data[freeIdx], n);
                Marshal.WriteInt32(hdr[freeIdx], 8, n * 2);  // dwBufferLength
                waveOutWrite(hWave, hdr[freeIdx], HDR_SIZE);
                used[freeIdx] = true;
            } else {
                Thread.Sleep(3);
            }
        }
    }
}

// ===== keyboard simulation: hold/release [Right Alt + Comma] (scan-code + dual API) =====
class KeySim {
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")]
    static extern short MapVirtualKey(ushort uCode, uint uMapType);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;
    const ushort VK_RMENU = 0xA5;       // Right Alt
    const ushort VK_OEM_COMMA = 0xBC;   // ','
    const ushort VK_F5 = 0x74;          // remote voice button = F5 (HID)
    const uint MAPVK_VK_TO_VSC = 0;

    static void Send(ushort vk, bool down) {
        byte sc = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE;
        if (vk == VK_RMENU) flags |= KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= KEYEVENTF_KEYUP;

        // Voice hotkey: retain the proven SendInput + keybd_event dual path.
        ushort[] single = new[] { vk };
        if (down) KeyComboSender.Press(single); else KeyComboSender.Release(single);
        keybd_event((byte)vk, sc, flags, UIntPtr.Zero);
    }
    public static void PressMappedCombo(ushort[] combo) {
        if (!KeyComboSender.Press(combo)) Console.WriteLine("[KEYMAP] SendInput down failed");
    }
    public static void ReleaseMappedCombo(ushort[] combo) {
        if (!KeyComboSender.Release(combo)) Console.WriteLine("[KEYMAP] SendInput up failed");
    }
    public static void TapMappedCombo(ushort[] combo) {
        if (!KeyComboSender.Tap(combo)) Console.WriteLine("[KEYMAP] SendInput tap failed");
    }
    public static void OpenTaskView() {
        System.Diagnostics.Process.Start("explorer.exe", "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}");
    }
    public static void DiagFg(string tag) {
        // kept for manual debugging; disabled by default
        if (Environment.GetEnvironmentVariable("REMOTEMIC_KEYDIAG") == "1") {
            IntPtr fg = GetForegroundWindow();
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(fg, sb, 256);
            uint pid; GetWindowThreadProcessId(fg, out pid);
            string proc = "?";
            try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
            Console.WriteLine("[KEY] "+tag+" | fgproc="+proc+" title="+(sb.ToString()==""?"(none)":sb.ToString()));
        }
    }
    public static void HoldCombo() {
        // The remote spams F5 while held. Our WH_KEYBOARD_LL hook marshals ALL
        // input (including our injected RAlt/Comma) through the pump thread, and
        // the heavy F5 traffic disrupts injection timing. So: temporarily remove
        // the hook, force-release every key for a clean slate, inject, then let the
        // pump thread re-install the hook.
        F5Blocker.Suspend();
        keybd_event((byte)VK_F5, (byte)MapVirtualKey(VK_F5, MAPVK_VK_TO_VSC), KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event((byte)VK_RMENU, (byte)MapVirtualKey(VK_RMENU, MAPVK_VK_TO_VSC), KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event((byte)VK_OEM_COMMA, (byte)MapVirtualKey(VK_OEM_COMMA, MAPVK_VK_TO_VSC), KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(50);
        Send(VK_RMENU, true);
        Thread.Sleep(40);
        Send(VK_OEM_COMMA, true);
        F5Blocker.Resume();
        Console.WriteLine("[KEY] HOLD done");
    }
    public static void ReleaseCombo() {
        F5Blocker.Suspend();
        Send(VK_OEM_COMMA, false);
        Thread.Sleep(20);
        Send(VK_RMENU, false);
        F5Blocker.Resume();
        Console.WriteLine("[KEY] RELEASE done");
    }
}
