using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public static class KeyMapUi {
    const int Port = 27170;
    static Thread uiThread;
    static NotifyIcon tray;
    static HttpListener listener;
    static Form staHost;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static volatile bool running;

    public static void Start() {
        if (uiThread != null) return;
        uiThread = new Thread(UiMain);
        uiThread.IsBackground = true;
        uiThread.Name = "keymap-ui";
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
    }

    public static void Stop() {
        running = false;
        try { if (listener != null && listener.IsListening) listener.Stop(); } catch { }
        try {
            if (staHost != null && staHost.IsHandleCreated)
                staHost.BeginInvoke(new Action(Shutdown));
            else
                Shutdown();
        } catch { }
    }

    static void UiMain() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try {
            staHost = new Form();
            staHost.ShowInTaskbar = false;
            staHost.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            staHost.Opacity = 0;
            staHost.Size = new Size(1, 1);
            staHost.StartPosition = FormStartPosition.Manual;
            staHost.Location = new Point(-4000, -4000);
            staHost.Show();
            staHost.Hide();

            tray = new NotifyIcon();
            tray.Icon = LoadAppIcon();
            tray.Text = "RemoteMic";
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowPanel(); };
            var menu = new ContextMenu();
            menu.MenuItems.Add("打开按键面板", delegate { ShowPanel(); });
            menu.MenuItems.Add("恢复默认映射", delegate { RestoreDefaults(); });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("退出", delegate { RemoteMic.RequestExit(); });
            tray.ContextMenu = menu;

            StartListener();
            Application.Run();
        } catch (Exception ex) {
            Console.WriteLine("[UI] failed: " + ex.Message);
        } finally {
            Shutdown();
        }
    }

    static void StartListener() {
        listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
        listener.Start();
        running = true;
        listener.BeginGetContext(OnRequest, null);
        Console.WriteLine("[UI] panel http://127.0.0.1:" + Port + "/");
    }

    static void OnRequest(IAsyncResult ar) {
        if (!running || listener == null) return;
        HttpListenerContext ctx = null;
        try { ctx = listener.EndGetContext(ar); }
        catch { }
        try { if (running && listener.IsListening) listener.BeginGetContext(OnRequest, null); } catch { }
        if (ctx == null) return;
        try { Handle(ctx); }
        catch (Exception ex) {
            try { WriteText(ctx.Response, 500, "text/plain; charset=utf-8", ex.Message); } catch { }
        }
    }

    static void Handle(HttpListenerContext ctx) {
        string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = "/";
        string method = ctx.Request.HttpMethod.ToUpperInvariant();

        if (method == "GET" && (path == "/" || path == "/index.html")) {
            WriteText(ctx.Response, 200, "text/html; charset=utf-8", ReadHtml());
            return;
        }
        if (method == "GET" && (path == "/favicon.ico" || path == "/app.ico")) {
            string ico = FindUiFile("app.ico");
            if (ico == null) { WriteText(ctx.Response, 404, "text/plain; charset=utf-8", "no icon"); return; }
            WriteBytes(ctx.Response, 200, "image/x-icon", File.ReadAllBytes(ico));
            return;
        }
        if (method == "GET" && path == "/app.png") {
            string png = FindUiFile("app.png");
            if (png == null) { WriteText(ctx.Response, 404, "text/plain; charset=utf-8", "no icon"); return; }
            WriteBytes(ctx.Response, 200, "image/png", File.ReadAllBytes(png));
            return;
        }
        if (method == "GET" && (path == "/remote.png" || path == "/remote.jpg")) {
            string img = FindUiFile("remote.png") ?? FindUiFile("remote.jpg");
            if (img == null) { WriteText(ctx.Response, 404, "text/plain; charset=utf-8", "no photo"); return; }
            string type = img.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            WriteBytes(ctx.Response, 200, type, File.ReadAllBytes(img));
            return;
        }
        if (method == "GET" && path == "/api/state") {
            WriteJson(ctx.Response, 200, BuildState());
            return;
        }
        if (method == "POST" && path == "/api/state") {
            var dto = Json.Deserialize<PanelStateDto>(ReadBody(ctx.Request));
            ApplyState(dto);
            WriteJson(ctx.Response, 200, BuildState());
            return;
        }
        if (method == "POST" && path == "/api/defaults") {
            KeyMapper.SaveAndReload(RemoteCatalog.DefaultBindings(), true);
            WriteJson(ctx.Response, 200, BuildState());
            return;
        }
        if (method == "POST" && path == "/api/pick-exe") {
            string picked = null;
            Exception err = null;
            staHost.Invoke(new Action(delegate {
                try {
                    using (var dlg = new OpenFileDialog()) {
                        dlg.Title = "选择要启动的程序";
                        dlg.Filter = "程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|所有文件 (*.*)|*.*";
                        if (dlg.ShowDialog() == DialogResult.OK) picked = dlg.FileName;
                    }
                } catch (Exception ex) { err = ex; }
            }));
            if (err != null) throw err;
            WriteJson(ctx.Response, 200, new PickDto { path = picked ?? "" });
            return;
        }

        WriteText(ctx.Response, 404, "text/plain; charset=utf-8", "not found");
    }

    static PanelStateDto BuildState() {
        var loaded = new Dictionary<ushort, KeyBinding>();
        foreach (KeyBinding b in RemoteCatalog.Merge(KeyMapper.Snapshot()))
            loaded[b.SourceVk] = b;

        var keys = new List<KeyDto>();
        foreach (RemoteKeyDef def in RemoteCatalog.All) {
            KeyBinding b;
            if (!loaded.TryGetValue(def.SourceVk, out b))
                b = new KeyBinding(def.Name, def.SourceVk, new ushort[0]);
            var dto = new KeyDto();
            dto.id = def.Id;
            dto.vk = def.SourceVk;
            dto.title = def.Title;
            dto.mode = def.Mode == RemoteKeyMode.Voice ? "voice" :
                (def.Mode == RemoteKeyMode.Native ? "native" : "editable");
            dto.click = ToSlot(b.ClickAction, b.Combo, b.ClickCommand, b.HasClick);
            dto.dbl = ToSlot(b.DoubleAction, b.DoubleCombo, b.DoubleCommand, b.HasDouble);
            if (b.HasRepeat) {
                dto.hold = ToSlot(b.RepeatAction, b.RepeatCombo, b.RepeatCommand, true);
                dto.repeat = true;
            } else {
                dto.hold = ToSlot(b.LongAction, b.LongCombo, b.LongCommand, b.HasLong);
                dto.repeat = false;
            }
            keys.Add(dto);
        }
        return new PanelStateDto { enabled = KeyMapper.Enabled, keys = keys };
    }

    static SlotDto ToSlot(KeyActionKind kind, ushort[] combo, string command, bool present) {
        var s = new SlotDto();
        if (!present) {
            s.kind = "empty";
            s.label = "未设置";
            return s;
        }
        if (kind == KeyActionKind.TaskView) {
            s.kind = "taskview";
            s.label = "任务视图";
            return s;
        }
        if (kind == KeyActionKind.Code) {
            s.kind = "code";
            s.command = command;
            s.label = "代码";
            return s;
        }
        if (kind == KeyActionKind.Launch) {
            s.kind = "launch";
            s.command = command;
            s.label = "打开 " + KeyMapConfig.LaunchDisplayName(command);
            return s;
        }
        if (kind == KeyActionKind.Cmd) {
            s.kind = "cmd";
            s.command = command;
            s.label = command;
            return s;
        }
        s.kind = "combo";
        s.combo = KeyMapConfig.FormatCombo(combo);
        s.label = KeyMapConfig.FormatDisplay(kind, combo, command);
        return s;
    }

    static void ApplyState(PanelStateDto dto) {
        if (dto == null || dto.keys == null) return;
        var incoming = new Dictionary<string, KeyDto>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyDto k in dto.keys) if (k != null && k.id != null) incoming[k.id] = k;

        var list = new List<KeyBinding>();
        foreach (RemoteKeyDef def in RemoteCatalog.FileOrder) {
            KeyBinding b = new KeyBinding(def.Name, def.SourceVk, new ushort[0]);
            KeyDto src;
            if (def.Mode == RemoteKeyMode.Native) {
                if (def.Id == "ok") { /* empty = native enter */ }
                else if (def.Id == "up") ApplyCombo(b, "UP", false);
                else if (def.Id == "down") ApplyCombo(b, "DOWN", false);
                else if (def.Id == "left") ApplyCombo(b, "LEFT", false);
                else if (def.Id == "right") ApplyCombo(b, "RIGHT", false);
            } else if (def.Mode == RemoteKeyMode.Editable && incoming.TryGetValue(def.Id, out src)) {
                ApplySlot(b, src.click, SlotKind.Click);
                ApplySlot(b, src.dbl, SlotKind.Double);
                ApplySlot(b, src.hold, src.repeat ? SlotKind.Repeat : SlotKind.Hold);
            }
            list.Add(b);
        }
        KeyMapper.SaveAndReload(list, dto.enabled);
    }

    enum SlotKind { Click, Double, Hold, Repeat }

    static void ApplySlot(KeyBinding b, SlotDto slot, SlotKind which) {
        KeyActionKind kind;
        ushort[] combo;
        string command;
        DecodeSlot(slot, out kind, out combo, out command);
        bool has = KeyBinding.HasPayload(kind, combo, command);
        if (which == SlotKind.Click) {
            b.ClickAction = kind;
            b.Combo = combo;
            b.ClickCommand = command;
            b.Tap = has && (kind != KeyActionKind.Combo || combo.Length > 1 || IsModifier(combo));
        } else if (which == SlotKind.Double) {
            b.DoubleAction = kind;
            b.DoubleCombo = combo;
            b.DoubleCommand = command;
            b.DoubleMs = has ? KeyMapConfig.DefaultDoubleMs : 0;
        } else if (which == SlotKind.Repeat) {
            b.RepeatAction = kind;
            b.RepeatCombo = combo;
            b.RepeatCommand = command;
            b.RepeatDelay = has ? KeyMapConfig.DefaultRepeatDelay : 0;
            b.RepeatInterval = has ? KeyMapConfig.DefaultRepeatInterval : 0;
            b.LongPressMs = 0;
            b.LongCombo = null;
            b.LongCommand = null;
        } else {
            b.LongAction = kind;
            b.LongCombo = combo;
            b.LongCommand = command;
            b.LongPressMs = has ? KeyMapConfig.DefaultHoldMs : 0;
            b.RepeatDelay = 0;
            b.RepeatInterval = 0;
            b.RepeatCombo = null;
            b.RepeatCommand = null;
        }
    }

    static void DecodeSlot(SlotDto slot, out KeyActionKind kind, out ushort[] combo, out string command) {
        kind = KeyActionKind.Combo;
        combo = new ushort[0];
        command = null;
        if (slot == null || String.IsNullOrEmpty(slot.kind) || slot.kind == "empty") return;
        if (slot.kind == "taskview") { kind = KeyActionKind.TaskView; return; }
        if (slot.kind == "code") { kind = KeyActionKind.Code; command = slot.command; return; }
        if (slot.kind == "launch") { kind = KeyActionKind.Launch; command = slot.command; return; }
        if (slot.kind == "cmd") { kind = KeyActionKind.Cmd; command = slot.command; return; }
        if (!String.IsNullOrWhiteSpace(slot.combo)) {
            ushort[] parsed;
            if (KeyMapConfig.TryParseCombo(slot.combo, out parsed)) combo = parsed;
            else Console.WriteLine("[UI] unknown combo: " + slot.combo);
        }
    }

    static void ApplyCombo(KeyBinding b, string name, bool tap) {
        ushort[] combo;
        if (!KeyMapConfig.TryParseCombo(name, out combo)) return;
        b.ClickAction = KeyActionKind.Combo;
        b.Combo = combo;
        b.Tap = tap;
    }

    static bool IsModifier(ushort[] combo) {
        if (combo == null || combo.Length == 0) return false;
        ushort vk = combo[0];
        return vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5 || vk == 0x5B || vk == 0x5C;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    const uint SWP_NOZORDER = 0x0004;

    static void ShowPanel() {
        const int width = 980, height = 600;
        Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int x = wa.Left + (wa.Width - width) / 2;
        int y = wa.Top + (wa.Height - height) / 2;
        string url = "http://127.0.0.1:" + Port + "/";
        string edge = FindEdge();
        try {
            if (edge != null) {
                var psi = new ProcessStartInfo();
                psi.FileName = edge;
                psi.Arguments = "--app=\"" + url + "\" --window-size=" + width + "," + height +
                    " --window-position=" + x + "," + y +
                    " --user-data-dir=\"" + Path.Combine(Path.GetTempPath(), "RemoteMicPanel") + "\"";
                psi.UseShellExecute = false;
                Process.Start(psi);
                ThreadPool.QueueUserWorkItem(delegate {
                    for (int i = 0; i < 25; i++) {
                        Thread.Sleep(80);
                        IntPtr hwnd = FindWindow(null, "RemoteMic");
                        if (hwnd != IntPtr.Zero) {
                            SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER);
                            break;
                        }
                    }
                });
            } else {
                Process.Start(url);
            }
        } catch (Exception ex) {
            Console.WriteLine("[UI] open panel: " + ex.Message);
            try { Process.Start(url); } catch { }
        }
    }

    static string FindEdge() {
        string[] paths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft\\Edge\\Application\\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft\\Edge\\Application\\msedge.exe")
        };
        foreach (string p in paths) if (File.Exists(p)) return p;
        return null;
    }

    static void RestoreDefaults() {
        if (MessageBox.Show("恢复默认按键映射？", "RemoteMic", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        KeyMapper.SaveAndReload(RemoteCatalog.DefaultBindings(), true);
    }

    static Icon LoadAppIcon() {
        string path = FindUiFile("app.ico");
        if (path != null) {
            try { return new Icon(path); } catch { }
        }
        return SystemIcons.Application;
    }

    static string FindUiFile(string name) {
        string[] candidates = {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui", name),
            Path.Combine(Directory.GetCurrentDirectory(), "ui", name)
        };
        foreach (string p in candidates)
            if (File.Exists(p)) return p;
        return null;
    }

    static string ReadHtml() {
        string path = FindUiFile("keymap.html");
        if (path != null) return File.ReadAllText(path, Encoding.UTF8);
        return "<!doctype html><meta charset=utf-8><body style='font-family:sans-serif;padding:40px'>找不到 ui/keymap.html</body>";
    }

    static string ReadBody(HttpListenerRequest req) {
        using (var r = new StreamReader(req.InputStream, Encoding.UTF8, true))
            return r.ReadToEnd();
    }

    static void WriteJson(HttpListenerResponse res, int code, object obj) {
        WriteText(res, code, "application/json; charset=utf-8", Json.Serialize(obj));
    }

    static void WriteText(HttpListenerResponse res, int code, string type, string text) {
        WriteBytes(res, code, type, Encoding.UTF8.GetBytes(text ?? ""));
    }

    static void WriteBytes(HttpListenerResponse res, int code, string type, byte[] buf) {
        res.StatusCode = code;
        res.ContentType = type;
        res.ContentEncoding = Encoding.UTF8;
        res.Headers["Cache-Control"] = "no-store";
        res.ContentLength64 = buf.Length;
        res.OutputStream.Write(buf, 0, buf.Length);
        res.OutputStream.Close();
    }

    static void Shutdown() {
        running = false;
        try { if (listener != null && listener.IsListening) listener.Stop(); } catch { }
        try { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } } catch { }
        try { Application.ExitThread(); } catch { }
    }

    public class PanelStateDto {
        public bool enabled;
        public List<KeyDto> keys;
    }
    public class KeyDto {
        public string id;
        public int vk;
        public string title;
        public string mode;
        public SlotDto click;
        public SlotDto dbl;
        public SlotDto hold;
        public bool repeat;
    }
    public class SlotDto {
        public string kind;
        public string combo;
        public string command;
        public string label;
    }
    public class PickDto { public string path; }
}
