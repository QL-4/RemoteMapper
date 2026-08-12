# 小米蓝牙语音遥控器 → PC 麦克风项目笔记

## 设备
- 名称: 小米蓝牙语音遥控器
- BLE MAC 前缀: C0:5D:39 (Xiaomi OUI；完整 MAC 属个人设备标识，已从仓库历史中脱敏)
- 适配器: 本机蓝牙适配器 MAC（未公开）
- BLE Device Id: `BluetoothLE#BluetoothLE<适配器MAC>-c0:5d:39:<剩余>`，按厂商前缀匹配
- HID 层 VID/PID: 2717 / 32B8

## GATT 服务全景
| Service UUID | 身份 | 备注 |
|---|---|---|
| 1812 (HID) | 键盘/按键 | Windows HID 栈占用 (AccessDenied)。9 个普通键可见；返回/音量±因非标准 Keyboard Page usage 被 `kbdhid.sys` 丢弃，最终由设备专属 lower filter 修复 |
| **ab5e0001** | **ATVV 语音** | **核心目标，完全可访问** |
| 8a7a0001 | 小米私有 | 备用 |
| 180f/180a/1800/1801 | 电池/设备信息/通用访问/通用属性 | 标准 |
| fe59 | Nordic DFU | 固件升级，无关 |

## ATVV 服务 (ab5e0001-5a21-4f05-bc7d-af01f617b664)
| Char | UUID 末段 | 属性 | 用途 |
|---|---|---|---|
| TX/CMD | ab5e0002 | Write | 主机→遥控器 命令 |
| RX/AUDIO | ab5e0003 | Notify | 遥控器→主机 ADPCM 音频流 |
| CTL | ab5e0004 | Notify | 遥控器→主机 控制信号 |

## 已验证的握手流程 (实测通过)
1. 订阅 ab5e0003 + ab5e0004 的 notify
2. Write ab5e0002 = `0A 01 00 00 03 03` (GET_CAPS v1.0)
   → 收 CTL: `0B 01 00 00 03 00 78 00 00`
     - 版本 1.0
     - codecs 0x0003 (支持 8kHz + 16kHz)
     - frame_size = 0x0078 = **120 字节**
3. Write ab5e0002 = `0C 00` (MIC_OPEN v1.0)
   → 收 CTL: `04 00 02 00` (AUDIO_START, codec=0x02=**16kHz ADPCM**)
4. **按住语音键** → 收 CTL `04 03 02 01` (AUDIO_START, reason=HTT) → 开始推 120B 音频帧
5. **松开** → 收 CTL `00 02` (AUDIO_STOP, reason=HTT release)
6. Write ab5e0002 = `0D` (MIC_CLOSE) → 收 CTL `00 00`

## ATVV opcode 表
TX (ab5e0002 Write, 主机→遥控器):
- 0x0A GET_CAPS
- 0x0C MIC_OPEN
- 0x0D MIC_CLOSE
- 0x0E MIC_EXTEND (keepalive, v1.0)

CTL (ab5e0004 Notify, 遥控器→主机):
- 0x00 AUDIO_STOP
- 0x04 AUDIO_START
- 0x08 START_SEARCH
- 0x0A AUDIO_SYNC
- 0x0B CAPS_RESP
- 0x0C MIC_OPEN_ERROR

## 音频参数 (Phase 2 已锁定，实测确认 ✅)
- 编码: IMA/DVI ADPCM, 4 bit/sample
- Nibble 顺序: **高 nibble 在前 (hi-first)**
- 采样率: **16 kHz** (16k 播放语速音高完全正确；8k 会变慢变低)
- 声道: mono
- 帧长: 120 字节/帧 = 240 samples/帧 ≈ 15ms
- **帧格式: HEADERLESS (无帧头！)**
  - 不同于标准 ATVV v0.4 的 6 字节头 (seq|pad|pred|step)
  - ByteUnique 分析证明: 全部 120 字节多样性一致 (~100 唯一值/40%)，无任何常数字节
  - 整帧 120 字节全是 ADPCM 数据
- **解码状态: 跨帧连续 (不重置 predictor/step_index)**
  - 初始 predictor=0, step_index=0
  - 正确解码签名: RMS≈349, 削波 0% (错误的重置/skip 解码 RMS≈18400 削波 62%)

## 解码后处理管线 (ATVVoice 同款, 逐项验证有效)
1. **Declip** (尖峰替换): 单点尖峰 (邻居差 >1000 且 >2× 邻居间差) → 用邻居均值替换。消除帧边界 click
2. **Lowpass** (3-tap 三角 FIR): `out[i] = (prev + 2*cur + next) >> 4`... 实为 `>>2`，即 [0.25,0.5,0.25]。去除高频量化噪声
3. **归一化**: p95 百分位抗尖峰归一化 (target = 0.5×32767)

## Phase 2 排错历程 (供参考，勿再走弯路)
- ❌ 标准 ATVV v0.4 6字节头 (pred@3-4BE step@5): ProbeHdr matchErr≈19000(随机水平)，ByteUnique 无常数字节 → 此遥控器无标准头
- ❌ per-frame 重置 predictor/step: 产生帧边界 click (70Hz 嗡嗡)，RMS 偏高
- ❌ skip N 字节连续解码: 丢失数据+错位；skip5 的 pitch=0.973 是 DC 漂移伪周期，非真语音
- ❌ lo-nibble-first: RMS=1239 (hi=349 的 3.5×)，非正确序
- ✅ headerless + 连续状态 + hi-first: 唯一通过 RMS/削波/人耳三重验证

## 路线图
- [x] Phase 1: 握手验证 (GET_CAPS/MIC_OPEN/音频流) ✅
- [x] Phase 2: IMA ADPCM 解码 → 16bit PCM ✅ (headerless 连续解码, CleanDecode.cs)
- [x] Phase 3: 实时解码管道 (BLE notify → ADPCM 解码 → VB-Cable) ✅ (src\RemoteMic.cs)
- [x] Phase 4: 联动语音输入法 (按住录入) ✅
- [x] Phase 5: HID 设备专用键隔离（音量±/返回/主页/菜单/直播/电源 -> F13-F19）✅
- [x] Phase 6: 通用 KeyMapper（配置文件驱动的全局组合键映射）✅

---

## Phase 3+4 最终实现 (src\RemoteMic.cs)

### 架构
单个程序 `RemoteMic.exe` 同时做三件事：
1. **BLE ATVV 连接** — 握手 + CTL 监听 (HTT 按下/松开) + AUDIO notify 实时解码
2. **音频推流** — 解码后 PCM → AGC → 写入 VB-CABLE Input (winmm waveOut, 8×双缓冲)
3. **热键联动** — 按住语音键 → 注入 [右Alt+逗号] down + 开始推流；松开 → keyup + 停止推流

### 解码+后处理 (实时, 每帧 120B)
1. IMA ADPCM headerless 连续解码 (hi-nibble-first, predictor/step 跨帧连续)
2. Declip (单点尖峰 → 邻居均值, 跨帧连续)
3. Lowpass 3-tap `[0.25,0.5,0.25]` (跨帧连续)
4. **AGC** (实时自适应增益): 跟踪峰值 `agcPeak`, 衰减系数 0.9997, 增益 = min(30, 28000/max(agcPeak,200)), 零削波。实测 RMS=6020 max=28008

### 关键问题与解决方案 (Phase 4 排错核心)

#### 问题1: 后台启动方式
- 早期使用 `Start-Process -RedirectStandardOutput` 调试时曾出现 SendInput 无效，因此一度误判为“必须前台运行”
- 最终验证：窗口可见性不影响钩子或 SendInput；关键是进程运行在当前交互用户会话，并由内部 pump 线程维护消息循环
- 当前方案：前台调试用 `debug.bat`；后台常驻用 `start.vbs` 隐藏窗口启动，日志由程序自身写入 `RemoteMic.log`，不要用 PowerShell 重定向 stdout 代替

#### 问题2: 注入线程上下文
- 怀疑过 WinRT 回调线程上下文问题, 实测可排除
- 最终采用独立 keyworker 线程 (BlockingCollection 传信号), GATT 回调只入队

#### ★★★ 问题3: 遥控器语音键 = F5 HID 干扰 (根因!)
- **发现工具**: `KeySniffer.cs` (WH_KEYBOARD_LL 全局钩子) + `KeyStateCheck.cs` (注入对照)
- **现象**: 遥控器按键触发注入后 WeType 不弹；但 (1) 物理键 [RAlt+逗号] 正常 (2) 无钩子进程注入正常 (3) 进程内无 F5 流时注入也正常
- **根因 (最终确认)**: 遥控器语音键被 Windows HID 栈映射为 **F5 键 (VK_0x74)**，按住时持续重发 F5。`WH_KEYBOARD_LL` 钩子在线时，**所有输入（包括 SendInput 注入的键）都会被 marshaling 到安装钩子的线程处理**；F5 高频流量拖垮了注入的 RAlt/Comma 时序 → WeType 检测不到干净热键组合
- **解决方案**: `KeySim.HoldCombo/ReleaseCombo` 注入前 `F5Blocker.Suspend()`（卸钩子，跨线程安全）→ 强制释放 F5+所有修饰键 → 注入 RAlt+Comma → `F5Blocker.Resume()`
- ★ **关键坑1: Resume 必须在 pump 线程执行** — `SetWindowsHookEx` 必须在跑消息泵的线程调用。用 `PostThreadMessage(pumpTid, WM_APP_REHOOK)` 让 pump 线程在 `GetMessage` 循环里重挂钩子，这样重挂的是活钩子（F5 count 持续增长验证）。在 keyworker 线程挂钩 = 死钩子（count 不涨）
- ★ **关键坑2: UnhookWindowsHookEx 跨线程安全** — Suspend 从 keyworker 线程调用 `UnhookWindowsHookEx(hhk)` 是安全的（任何线程都能卸钩子）
- **证据**: Suspend/Resume 版本 F5 count 持续增长 (1→75→144→260→350) 且 WeType 每次触发；钩子全程在线版 count 也不涨且不触发
- **教训**: (1) 任何「注入热键不生效但物理键正常」第一步看设备发了什么 HID (2) **WH_KEYBOARD_LL 钩子会改变整个进程的输入处理路径，高频 HID 设备 + 同时注入热键 = 灾难**，注入期间必须卸钩子

### 最终热键注入方案 (KeySim)
- `SendInput` + `keybd_event` 双保险
- 带 scan code (`KEYEVENTF_SCANCODE`) + 扩展键标志 (`KEYEVENTF_EXTENDEDKEY` for RAlt)
- HoldCombo: **先 `F5Blocker.Suspend()` 卸钩子** → 强制释放 F5+RAlt+逗号 → Send RAlt down → Send 逗号 down → `F5Blocker.Resume()`
- ReleaseCombo: **先 Suspend** → Send 逗号 up → Send RAlt up → Resume
- ★ Suspend/Resume 是注入生效的**唯一关键**（不是强制释放 F5 本身）

### 麦克风路由
- VB-Cable: 播放端 `CABLE Input` (winmm waveOut), 录音端 `CABLE Output`
- 微信输入法 (WeType) 只能用系统默认录音设备
- **自动切换方案** (已实现): 按住语音键 → `DeviceSwitch.SwitchToCable()` 把系统默认录音切到 CABLE Output；松开 → `Restore()` 切回原设备。通过 `IPolicyConfig` COM 接口的 `SetDefaultEndpoint` 实现
- COM 调用在 STA 线程执行 (OnSta 包装)
- **只在遥控器触发时切换** (ACT_HOLD/RELEASE 走 HTT 通道), 手动操作不受影响

### DeviceSwitch 实现要点 (IPolicyConfig COM)
- CLSID `870af99c-171d-4f9e-af0d-e63df40c2bc9` (CPolicyConfigClient), 接口 `f8679f50-850a-41cf-9c72-430f290290c8`
- **关键: SetDefaultEndpoint 是第 11 个方法** (前 10 个占位: GetMixFormat...SetPropertyValue)
  - llm 给的 21 个占位是错的! (混淆了接口版本) → AccessViolation
  - 正确数法见 web-research 结果
- `new CPolicyConfigClient()` 然后 `SetDefaultEndpoint(id, role)` 三个 role 都调 (eConsole/eMultimedia/eCommunications)
- 设备枚举/取名用 `IMMDeviceEnumerator` + `IPropertyStore`, friendly name (VT_LPWSTR) 读 offset 8 的 IntPtr, 读后 `PropVariantClear`
- COM 调用应在 STA 线程, 本实现用 OnSta 包装 (SetApartmentState + Join)

### 运行方式
```bash
REMOTEMIC_HOTKEY=1   # 默认开启热键
REMOTEMIC_DUMP=1     # 可选: 保存实时解码 WAV 到 rt_dump_*.wav
REMOTEMIC_KEYDIAG=1  # 可选: 打印注入时前台窗口
RemoteMic.exe        # 前台调试用；后台用 start.vbs（隐藏窗口，钩子靠内部 pump 线程，无需可见窗口）
```

### 编译命令

```bat
build.bat
```

脚本会编译 `RemoteMic.cs`、`KeyMapConfig.cs`、`KeyMapEngine.cs`、`KeyMapper.cs` 和 `KeyComboSender.cs`，并写出 `RemoteMic.exe`。

## Phase 5：HID 设备专用键隔离（最终结论）

### 问题

遥控器把三个特殊键放在 HID Keyboard Page：

```text
音量加  usage 0x80
音量减  usage 0x81
返回    usage 0xF1
```

Android `getevent` 能看到它们，但 Windows 用户态的低级键盘钩子、Raw Input、APPCOMMAND 和 HID 设备读取均没有事件。根因是 `kbdhid.sys` 不为这些 Keyboard Page usage 生成扫描码/VK。

### 设备报告

使用只读 preparsed metadata 工具确认：

- Top-level collection：`0x0001/0x0006`（Keyboard）
- `InputReportByteLength = 121`
- Keyboard Report ID：`0x01`

KMDF lower filter 的一次性内核诊断确认真实报告为：

```text
方向上  01 00 00 52 00 ...
音量加  01 00 00 80 00 ...
音量减  01 00 00 81 00 ...
返回    01 00 00 F1 00 ...
```

usage 位于 `report[3]`，不是 HID parser 合成报告时看起来的 `report[1]`。这一差异是调试中的关键结论。

### 最终实现

`driver/MiRemoteHidFilter` 是精确绑定 VID/PID/REV 的 KMDF device lower filter：

```text
kbdclass -> kbdhid -> MiRemoteHidFilter -> mshidumdf
```

它只拦截 `IRP_MJ_READ` 的完成路径，并等长修改 `report[3]`：

```text
0x80 -> 0x68 (F13 / VK 0x7C)  音量加
0x81 -> 0x69 (F14 / VK 0x7D)  音量减
0xF1 -> 0x6A (F15 / VK 0x7E)  返回
0x4A -> 0x6B (F16 / VK 0x7F)  主页
0x65 -> 0x6C (F17 / VK 0x80)  菜单
0x35 -> 0x6D (F18 / VK 0x81)  直播
0x66 -> 0x6E (F19 / VK 0x82)  电源
```

前三个 usage 是 `kbdhid.sys` 原本不生成 VK 的特殊 usage。后四个本可生成 Home / Apps / OEM_3 / Power，但 `WH_KEYBOARD_LL` 只给 VK、不提供 HID 来源设备 ID：直接把主页 `VK_HOME=0x24` 映射为 Win+Tab 时，物理键盘 Home 也会被吞掉。故将需要组合键映射的遥控器普通键也隔离为 F16-F19。物理 Home/Apps/反引号/Power 保持原行为。

实机验收工具检查方向上与 F13-F19 共八键；设备状态 `CM_PROB_NONE`，HVCI/内存完整性保持开启。当前包是 WDK 测试签名包，需要 TESTSIGNING。安装、回滚和正式签名限制见 `driver/MiRemoteHidFilter/README.md`。

## Phase 6：通用 KeyMapper

`RemoteMic.exe` 启动时读取根目录 `keymap.txt`。配置解析、按键状态机和 Windows hook/SendInput 分离：

- `KeyMapConfig`：解析源 VK 与目标组合，支持 A-Z、0-9、F1-F24、修饰键、方向键和常用 OEM 键
- `KeyMapEngine`：跳过空映射和同键映射；维护 held set，吞掉按住重复但只注入一次 down；up 时生成一次释放动作；忽略 injected event 防递归
- `F5Blocker`：原有全局低级钩子先吞 F5，再把其他物理键交给 KeyMapper
- `keyworker`：hook 回调只入队；worker 用单次 `SendInput` 按顺序按下组合、逆序释放

当前配置：

```text
电源键 (F19) 短按 -> LALT+X
电源键 (F19) 长按 800ms -> 立即点按 LALT+F4
返回键 (F15) 短按 -> LCTRL+Z
返回键 (F15) 长按 800ms -> LCTRL+LSHIFT+Z
主页键 (F16) 短按 -> LALT+TAB
主页键 (F16) 长按 800ms -> TASKVIEW
菜单键 (F17) -> 不映射
直播键 (F18) -> ESC
```

四个方向键配置为自身，状态机自动放行。音量±目前只由驱动修复为 F13/F14，目标映射留空。所有组合键映射都应以 filter 生成的 F13-F24 为源；不要把 Home、Apps、OEM_3、Enter 或方向键等共享 VK 直接写成映射源。

普通映射保持目标组合直到源键抬起；`TAP` 映射在源键抬起后原子点按。`HOLD <ms>` 由 hook pump 的 25ms Win32 timer 判定：到阈值立即触发一次 long action，继续按住不重复，松开不补发；阈值前松开则执行 short action。定时器使用 `SetTimer(NULL, ...)` 返回的实际 ID（Windows 不保证保留请求 ID）。

主页长按不能注入 Win：F16 仍由 HID 物理保持时，Windows 会识别保留快捷键 `Win+F16` 并显示“滑动以关闭电脑”。即使先通过 SendInput 注入 F16 key-up，也不能可靠取消底层物理状态。因此配置支持 `TASKVIEW` 系统动作，由 worker 执行 `explorer.exe shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}`，完全不发送 Win 键。

自动测试覆盖：配置解析、`TASKVIEW` 系统动作、短按/长按阈值、长按只触发一次、长按后下一次短按恢复、同键放行、重复 down 去重、up 释放、注入事件放行、物理 Home/Apps/反引号/Power 放行，以及真实 `SendInput` 的组合键按下/释放顺序。

## 技术栈约束
- 编译: .NET Framework 4.8 csc.exe (无 .NET SDK, 有 .NET 8 runtime)
- WinRT 引用: C:\Windows\System32\WinMetadata\*.winmd + GAC System.Runtime + System.Runtime.InteropServices.WindowsRuntime
- 关键坑:
  - .NET Framework 4.8 csc 对新 WinMetadata 的 WinRT 事件投影失效 → 用反射 HookEvent 调 add_ValueChanged
  - WinRT async awaiter 版本不匹配 → 用 TaskCompletionSource 手动包装 IAsyncOperation (AsT helper)
  - PowerShell 5.1 无法订阅 WinRT 事件 → 必须 C#
  - BLE Device Id 不含 VID/PID，按 MAC c0:5d:39 匹配


## 2026-08-11 修复: 物理键盘 F5 被 F5Blocker 吞掉 (问题3 的副作用)
- **现象**: RemoteMic 运行期间物理键盘 F5 完全失效（浏览器刷新、IDE 调试等全部无响应）。
- **根因**: 问题3 的 F5Blocker 钩子无条件吞掉所有 VK 0x74 (F5)——遥控器语音键的 HID F5 刷屏与物理键盘 F5 在 WH_KEYBOARD_LL 层完全无法区分，只能全吞。
- **修复**: 下沉到驱动层解决——`MiRemoteHidFilter` 把语音键 HID usage 0x3E (F5) 改写成 0x6F (F20)。本方案将 F20 保留为遥控器专用语音键（本机物理键盘不产生），因此:
  - `VoiceKeyBlocker`（原 F5Blocker）改吞 VK_F20 (0x83)，语音键刷屏依旧不落前台，物理 F5 完全放行；
  - `KeySim.HoldCombo` 注入前的强制释放键从 F5 改为 F20；
  - 驱动 INF 版本 1.0.1.0；`RemoteKeyTest` 九键测试增加语音键 F20。
- **部署**: 重新编译驱动 → `install-driver.ps1`（需 TESTSIGNING，装完重启）→ 重启 RemoteMic → `verify-keys.bat` 验证。

## 2026-08-12 ATVV 协议健壮性增强

参考 `HD838A/remote-mic-app`（GPL-3.0，仅看协议行为、未复制源码），在不改变现有行为的前提下增强 ATVV 音频链路的健壮性。所有改动在正常路径下与原版完全一致，只在边缘情况下提供额外保护：

1. **解析 CAPS 响应**：原版发 `GET_CAPS` 后固定等待 600ms 直接 `MIC_OPEN`，忽略响应内容。现在解析 opcode `0x0B` 响应，提取 version / codec / frameSize，存入 `frameSize` 变量。实测本机遥控器返回 v256 (0x0100)、codec=0x02 (16kHz)、frame=120，与硬编码默认值一致，行为不变。

2. **处理 AUDIO_SYNC**：原版完全忽略 opcode `0x0A`。现在解析其中的 predictor (bytes 4-5, 大端有符号) 和 stepIndex (byte 6)，在下一帧解码前重置 ADPCM 解码器状态。若 BLE 传输中丢包导致解码器偏差，同步包可重新校准；若设备不发同步包（当前情况），则无任何影响。

3. **帧累积器 (FrameAccumulator)**：原版假设每次 BLE audio notification 恰好 120 bytes。现在用 `List<byte>` 缓冲，每满 `frameSize` 取一帧解码。当 notification 被分片或合并时仍能正确解码。正常路径（恰好 120 bytes、无 pending）走快速路径，零额外开销。

4. **真实 session ID**：原版 `MIC_EXTEND` 固定用 session ID 0。现在从 `AUDIO_START` (opcode 0x04) 的 byte[3] 提取真实 session ID，用于 keepalive 的 `MIC_EXTEND`。本机遥控器 session ID 为 0 时行为不变。

5. **会话边界清理**：在 `AUDIO_START`、`AUDIO_STOP`、`MIC_CLOSED` 时清空帧累积器和同步标志，防止上一会话残留数据泄漏到新会话。

改动文件：`src/RemoteMic.cs`（新增字段 + `DecodeFrame` 方法、重构 `MakeAudioHandler`、增强 `MakeCtlHandler`）。

## 2026-08-13 手势引擎：双击 + 按住连发

参考 `HD838A/remote-mic-app` 的手势状态机设计，为 `keymap.txt` 增加 `DOUBLE` 和 `REPEAT` 手势类型。不改变任何现有映射的行为——只有显式配置了 `DOUBLE` 或 `REPEAT` 的键才会激活新逻辑。

### keymap.txt 新语法

```text
# 单击 + 双击 + 长按
菜单键 = 0x80 -> TAP X | DOUBLE 300 -> TAP Y | HOLD 550 -> TAP Z

# 单击 + 按住连发 (与 HOLD 互斥)
音量加 = 0x7C -> TAP BACK | REPEAT 350 100 -> TAP BACK
```

- **DOUBLE <ms>**：松开后等待 ms 毫秒，若无第二次按下则触发单击；有第二次按下则触发双击动作。单击动作因此被延迟。
- **REPEAT <delay> <interval>**：按住 delay 毫秒后触发第一次，之后每隔 interval 毫秒重复触发。松开即停。与 HOLD 互斥（解析器会报错）。
- **HOLD <ms>**：与现有行为一致，到阈值立即触发一次。
- DOUBLE 可以与 HOLD 或 REPEAT 共存。

### 状态机重构

`KeyMapEngine` 从三个并行集合 (`held`/`downTimes`/`longFired`) 重构为 per-key `KeyState` 对象，跟踪：
- `IsHeld` / `PressTime` / `Fired` / `NextRepeat`
- `WaitingDouble` / `DoubleDeadline` / `PressCount`

正常路径（无 DOUBLE/REPEAT 的键）行为与旧版完全一致：
- 非 delayed 键 → 立即 down/up（方向键、ESC 等）
- TAP 键 → 松开时原子点按
- HOLD 键 → 到阈值触发一次

### 代码结构

- `KeyMapConfig`：解析器拆分 `|` 段，按关键字分类（DOUBLE/HOLD/REPEAT），验证互斥
- `KeyMapEngine`：`HandleTimed` 处理 down/up 边沿，`TakeDueActions` 轮询长按/连发/双击超时
- 自动测试覆盖：DOUBLE 解析与检测、REPEAT 解析与定时、互斥校验、双击+长按共存、连发快速松开回退为单击、所有现有场景回归
