# RemoteMic 部署指南（新机器从零部署）

> 本指南面向「把项目搬到另一台 Windows 上跑起来」的场景。
> 日常使用文档见 [`README.md`](README.md)，技术实现细节见 [`NOTES.md`](NOTES.md)。

---

## 0. 部署概览

语音功能部署 = **拷 4 个 .exe + 装 2 个依赖软件 + 蓝牙配对遥控器**。运行时不依赖额外 dll —— WinRT / COM / .NET Framework 4.8 都由 Windows 系统自带提供。

若还需要让 Windows 识别遥控器的音量±和返回键，或要把主页/菜单/直播/电源映射为全局组合键，另行部署可选的 `driver/MiRemoteHidFilter`。它为遥控器分配 F13-F19 设备专用源键，避免误触物理键盘的同名键。当前包是测试签名内核驱动，需要 TESTSIGNING；完整步骤只维护在 [`driver/MiRemoteHidFilter/README.md`](driver/MiRemoteHidFilter/README.md)。

```
部署清单（最小集合）：

根目录（日常运行所需）：
  RemoteMic.exe        主程序（必需）
  keymap.json          按键映射（必需；若只有旧 keymap.txt 会自动迁移）
  ui\keymap.html       按键面板前端（必需）
  ui\remote.png        遥控器产品图（面板中间）
  start.vbs            后台启动器（推荐，无窗口常驻）
  stop.bat             停止后台进程
  debug.bat            前台启动脚本（可选，调试看实时输出）
  install-autostart.bat / uninstall-autostart.bat  开机自启 安装/卸载（可选）

tools\（按需诊断，非运行必需）：
  KeySniffer.exe       诊断：键盘事件抓取（建议）
  DefDev.exe           诊断：录音设备列举/切换（建议）
  CaptureCable.exe     诊断：CABLE 音频回路验证（建议）
  RemoteKeyTest.exe    可选驱动八键验收（方向上 + F13-F19）

可选驱动：
  driver\MiRemoteHidFilter\package\  测试签名安装包
  driver\MiRemoteHidFilter\*.bat     准备、安装、验证、卸载和恢复脚本
```

**数据流（理解原理有助于排错）：**

```
遥控器 ─蓝牙BLE─> RemoteMic 解码音频 ─推流─> CABLE Input(虚拟播放端)
                                              │（虚拟线内部传输）
                                              v
                              CABLE Output(虚拟录音端) <─ 程序按住时临时切为系统默认录音设备
                                              │
                              微信输入法 ←─ 读取"系统默认录音设备" ─ 录到遥控器的声音
                                ↑
                RemoteMic 注入 [右Alt+逗号] 热键 ─ 触发微信输入法语音录入
```

> ⚠️ **关键约束**：微信输入法**只能用"系统默认录音设备"**，不能在输入法里单独选设备。这就是为什么程序必须在使用时临时把默认设备切到 CABLE Output。

---

## 1. 环境要求（先检查）

| 项目 | 要求 | 检查方法 |
|------|------|----------|
| 操作系统 | Windows 10 / 11 (x64) | — |
| .NET Framework | 4.8（Win10 1903+ / Win11 自带） | 设置→应用，搜 ".NET Framework 4.8"；或运行 `RemoteMic.exe` 不报错即满足 |
| 蓝牙 | 支持 BLE（蓝牙 4.0+） | 设置→蓝牙，能发现遥控器即可 |
| 麦克风权限 | 允许应用使用麦克风 | 设置→隐私→麦克风，开启 |
| 权限 | 普通用户即可（**无需管理员**） | — |

---

## 2. 安装 VB-CABLE（虚拟音频线）

这是把"遥控器音频"接到"微信输入法麦克风"的桥梁。

1. 下载 **VB-Audio Virtual Cable**：https://vb-audio.com/Cable/ （免费）
2. 解压，右键 `VBCABLE_Setup_x64.exe` → **以管理员身份运行** → Install
3. **重启电脑**（或重启 Windows Audio 服务）
4. **验证**：右键任务栏喇叭 → 声音设置，应看到新增：
   - 播放设备里有 **`CABLE Input`**
   - 录音设备里有 **`CABLE Output`**

> 排错：如果只看到一个，重启后再看；仍不行重新运行安装程序。

---

## 3. 安装并配置微信输入法（WeType）

1. 下载安装：https://z.weixin.qq.com/
2. 安装后切到微信输入法，打开**设置**：
   - 找到「**语音输入**」功能，确保**开启**
   - 确认语音输入快捷键 = **右 Alt + 逗号（`,`）**（默认即是；如被占用请改回此项）
3. **验证快捷键**：在任意文本框按 `右Alt + 逗号`，应弹出语音录入浮窗并开始录音

> 排错：如果物理键都唤不起语音录入，检查微信输入法设置里语音输入是否开启、快捷键是否正确、是否有别的软件抢占了该快捷键。

---

## 4. 蓝牙配对遥控器

1. 设置 → 蓝牙和其他设备 → **添加设备** → 蓝牙
2. 选择「**小米蓝牙语音遥控器**」配对（配对码通常自动通过）
3. 配对成功后，遥控器会同时建立 **BLE 连接**（语音数据）和 **HID 键盘连接**（按键），这是正常的
4. **验证**：设备列表里能看到遥控器且显示"已连接"

> 遥控器会显示为一个 HID 键盘设备。配对后按遥控器语音键时，系统会收到 **F5 键**（这是正常的，程序会拦截它）。

---

## 5. 拷贝程序文件并放行

1. 在目标机器建一个文件夹，例如 `D:\RemoteMapper\`
2. 把部署清单所列文件拷进去，**保持目录结构**：根目录放 `RemoteMic.exe` + 脚本，`tools\` 子目录放 3 个诊断 exe
3. **放行 Defender**：首次运行若被 SmartScreen / Defender 拦截：
   - SmartScreen → 「更多信息」→ 「仍要运行」
   - 若被隔离 → Windows 安全中心 → 病毒和威胁防护 → 允许在设备上 / 添加排除项

---

## 6. 首次运行与验证（逐项确认）

**首次建议用前台方式排查**：双击 `RemoteMic.exe`（或 `debug.bat`）。按顺序确认每一行都出现：

| 顺序 | 预期输出 | 含义 | 不对怎么办 |
|------|----------|------|-----------|
| 1 | `[1/4] connecting to remote... OK (MI RC)` | 蓝牙连上遥控器 | 见下「remote NOT FOUND」 |
| 2 | `[2/4] setting up ATVV service... OK` | 语音协议就绪 | 重启程序再试 |
| 3 | `[3/4] opening VB-Cable Input... OK` | 找到虚拟线 | 确认第 2 步装好 CABLE |
| 4 | `[F5] blocker installed` | F5 拦截就绪 | 正常都会出现 |
| 5 | `[DEV] CABLE Output found as device` | 找到录音端设备 | 确认 CABLE Output 存在 |
| 6 | `[4/4] ATVV handshake... ready` | 握手完成 | 见下 |

看到 `>> HOLD the voice button to talk` 后：

7. 把光标点进任意**文本框**（记事本、聊天框、Word…）
8. **按住**遥控器语音键，说一句话
9. ✅ **微信输入法弹出语音录入，并转写出文字** = 成功！
10. **松开**语音键 → 录入结束，默认录音设备自动恢复

---

## 7. 部署专属故障排查

| 现象 | 原因 / 解决 |
|------|------------|
| `[1/4] remote NOT FOUND` | 遥控器未连接或休眠。按几下遥控器任意键唤醒，确认蓝牙列表里是"已连接"，再启动程序。若仍不行：删除配对重新配对 |
| `[2/4]` 反复重试失败 | BLE 服务未就绪，多发生在刚配对后。等 10 秒重启程序 |
| `[DEV] CABLE Output not found` | VB-CABLE 没装好或没重启。回到第 2 步确认录音设备里有 `CABLE Output` |
| 程序一切正常，但微信输入法**不弹出** | 先用**物理键盘**按 `右Alt+逗号`：手动也不弹 = 微信输入法设置问题（第 3 步）；手动能弹 = 重启 RemoteMic 再试 |
| 微信输入法弹出但**转写不出文字** | CABLE 音频回路没通。运行 `tools\CaptureCable.exe`（按住遥控器说话录 3 秒），回放听有没有声音 |
| 转写的声音很小/断续 | 对着遥控器麦克风说话；属正常（程序已做 AGC 自动增益） |
| 双击 exe 窗口一闪而过 | 改用 `debug.bat` 启动，窗口会停留显示错误信息 |
| 被杀毒软件删除 | 加信任 / 添加排除文件夹 |

---

## 附录 A：从源码重新编译

> 只有需要改代码时才需要。部署到新机器**无需编译**，直接拷贝现成的 `.exe` 即可。

**前置**：目标机器有 .NET Framework 4.8（Win10/11 自带 `csc.exe`，无需安装 SDK）。

```bat
cd /d D:\RemoteMapper
build.bat
```

`build.bat` 会编译主程序及 KeyMapper 的所有源文件，并引用所需 WinMetadata/System.Runtime。4 个系统引用缺一不可（Windows.Storage.winmd 提供 IBuffer；System.Runtime 提供异步扩展）。诊断工具（KeySniffer/DefDev/CaptureCable）是单文件，可用系统 `csc.exe` 单独编译。

---

## 附录 B：自定义配置

### B1. 换一个（另一个）小米遥控器

程序**按蓝牙设备名 "MI RC" 自动匹配**，所以换同型号遥控器**无需改代码**。直接配对新遥控器、运行程序即可。

> 若设备名不是 "MI RC"，改 `src\RemoteMic.cs` 中 `IndexOf("MI RC", ...)` 里的名字后重新编译。

### B2. 换一个语音输入法 / 换快捷键

语音键触发微信输入法的热键仍在 `src\RemoteMic.cs` 的 `KeySim` 中定义为右 Alt + 逗号。普通遥控器按键的全局映射则直接修改根目录 `keymap.json`，重启 RemoteMic 后生效，无需重新编译。

### B3. 不自动切换录音设备（手动固定）

如果不想让程序动系统默认设备（例如已用 per-app 方式让微信输入法录 CABLE），注释掉 `keyworker` 里 HOLD/RELEASE 分支的 `DeviceSwitch.SwitchToCable()` / `DeviceSwitch.Restore()` 即可。

---

## 附录 C：关键技术依赖（供排错理解）

- **BLE ATVV 协议**：小米遥控器的语音数据走蓝牙 GATT 服务 `ab5e0001-…`，IMA ADPCM @16kHz 编码，程序实时解码
- **VB-CABLE**：提供一对虚拟播放/录音设备，让程序能把解码音频"喂"给微信输入法
- **WH_KEYBOARD_LL 钩子**：拦截遥控器语音键发出的 F5（F5 会污染注入的热键）。注入热键时**临时卸载钩子**避免 marshaling 干扰，注入后由泵线程重新挂上
- **IPolicyConfig COM**：用于临时切换系统默认录音设备（仅在使用期间）
- **运行时零依赖**：编译后的 `.exe` 不携带任何外部 dll，所有依赖（WinRT/COM/.NET 4.8）均由 Windows 提供
