# 小米蓝牙语音遥控器 → 微信输入法语音录入

> 📌 **要在其他电脑上部署？** 请看 [`DEPLOY.md`](DEPLOY.md)（新机器从零部署指南）。

把小米蓝牙语音遥控器的语音键变成微信输入法（WeType）的语音输入按钮：
**按住遥控器语音键** → 自动唤起微信输入法语音录入 + 把遥控器麦克风的音频送进去；
**松开** → 结束录入，恢复系统原默认麦克风。

项目还包含一个设备专属 HID lower filter：既修复 Windows 会丢弃的三个按键，也为需要全局映射的遥控器普通键分配独有 F 键：

```text
音量加 -> F13    音量减 -> F14    返回键 -> F15
主页键 -> F16    菜单键 -> F17    直播键 -> F18    电源键 -> F19
语音键 -> F20（驱动把 HID F5 改成 F20，物理键盘 F5 不受影响）
```

驱动源码、安装和回滚说明见 [`driver/MiRemoteHidFilter/README.md`](driver/MiRemoteHidFilter/README.md)。`RemoteMic.exe` 启动时读取 `keymap.txt`，提供全局源键→组合键映射。当前配置：电源短按→Alt+Tab、长按→Task View；返回短按→Ctrl+Z、长按→Ctrl+Shift+Z；主页短按→Alt+X、长按→Alt+F4；音量加→Backspace；音量减→Delete；菜单不映射；直播→Esc。四个方向键保持原行为。

---

## 一、前置准备（只需做一次）

### 1. 安装 VB-Cable（虚拟音频线）
- 下载安装 [VB-Audio Virtual Cable](https://vb-audio.com/Cable/)（免费）
- 安装后系统会多出一对虚拟音频设备：
  - `CABLE Input`（播放端）
  - `CABLE Output`（录音端）
- **不要**手动修改系统默认录音设备 —— 程序会在使用时自动切换

### 2. 安装微信输入法
- 安装 [微信输入法（WeType）](https://z.weixin.qq.com/)
- 在微信输入法设置里，开启**语音输入**功能
- 确认语音输入的快捷键是 **右 Alt + 逗号（,）**（默认即是）

### 3. 配对遥控器
- 设置 → 蓝牙 → 添加「小米蓝牙语音遥控器」并配对
- 配对后遥控器会同时作为 **BLE 设备**（语音数据）和 **HID 键盘**（按键）连接

### 4. 放行程序
- 如果被杀毒软件/Windows Defender 拦截，请放行 `RemoteMic.exe`

---

## 二、启动

两种入口，按需选用：

### A. 后台常驻（推荐，无窗口）
- **双击 `start.vbs`** —— 无窗口后台启动，日志写入 `RemoteMic.log`
- 停止：双击 `stop.bat`（或 `taskkill /F /IM RemoteMic.exe`）
- 开机自启：双击 `install-autostart.bat`（卸载用 `uninstall-autostart.bat`）
- 程序已在跑时再启动会提示，不会重复开第二个

### B. 前台窗口（调试用，看实时输出）
- **双击 `debug.bat`**（前台调试看实时输出），或直接运行 `RemoteMic.exe`；`Ctrl+C` 退出

看到以下提示即表示就绪：

```
== RemoteMic: remote mic -> CABLE + WeChat IME hotkey ==
[1/4] connecting to remote... OK (MI RC)
[2/4] setting up ATVV service... OK
[3/4] opening VB-Cable Input... OK
[F20] voice-key blocker installed (remote voice key = F20, physical F5 passes)
[DEV] CABLE Output found as device; will auto-switch default capture while talking
[4/4] ATVV handshake... ready
>> HOLD the voice button to talk. Release to stop.
```

现在就可以用了：
1. 把光标点进任意**文本框**（聊天框、记事本、Word 等）
2. **按住**遥控器语音键，正常说话
3. 微信输入法弹出语音录入，转写文字输入到光标处
4. **松开**语音键结束

按 **Ctrl + C** 退出程序。

---

## 三、工作原理

```
按住语音键 ──┬─> BLE 通知 CTL「按下」
             │     ├─ 默认录音设备切换 → CABLE Output（让微信输入法能录到）
             │     ├─ 注入热键 [右Alt + 逗号] 按下 → 微信输入法弹出
             │     └─ 开始把遥控器音频解码后推送到 CABLE Input
             │
             └─> HID 同时发出 F5 → 被 F5Blocker 钩子吞掉（不干扰）

松开语音键 ──┬─> BLE 通知 CTL「松开」
             │     ├─ 停止推流
             │     ├─ 注入热键释放 → 微信输入法结束录入
             │     └─ 默认录音设备恢复 → 你的原麦克风
```

**核心要点：**
- 遥控器音频通过蓝牙 BLE 的 ATVV 协议传输（IMA ADPCM 编码），程序实时解码后通过 VB-Cable 虚拟线送给微信输入法
- 程序内部拦截了遥控器语音键会发出的 **F5** 键盘事件（它会污染热键组合），触发信号完全走 BLE 通道，不受影响
- **只在按住遥控器时**临时切换默认录音设备，松开后立即恢复，平时用别的麦克风完全不受影响

---

## 四、HID 设备专用键隔离（可选）

遥控器把音量加、音量减、返回放在 HID Keyboard Page 的 `0x80/0x81/0xF1` usage 上，Windows `kbdhid.sys` 默认不会生成按键事件。`driver/MiRemoteHidFilter` 在 `kbdhid` 解析前等长改写为 F13/F14/F15。

主页、菜单、直播、电源原本会分别成为 Home、Apps、反引号、Power；但 `WH_KEYBOARD_LL` 不提供来源设备 ID，若直接在 `keymap.txt` 映射它们，物理键盘上的同名键也会被误触发。因此 filter 还把**此遥控器的**四个 usage 改为 F16–F19。全局映射只监听 F13–F19，物理键盘的 Home/Apps/反引号/Power 保持原样。

实机验收工具会检查方向上 + F13–F19 共八键；HVCI/内存完整性可以保持开启。当前提交中的包使用 WDK 测试证书，因此需要 TESTSIGNING。详细步骤与回滚方式只维护在驱动目录的 README 中。

---

## 五、全局按键映射

`keymap.txt` 每行格式：

```text
<键名> = <源 VK> -> <目标组合>
```

目标组合使用 `+` 连接，例如 `LCTRL+Z`、`LALT+TAB`。留空表示不映射；目标只有一个键且与源 VK 相同也会自动放行。配置在 RemoteMic 启动时加载，修改后需重启 RemoteMic。

默认映射会在源键按住期间保持目标组合。`TAP` 表示等源键抬起后原子点按一次；`HOLD` 可配置长按阈值：

```text
电源键 = 0x82 -> TAP LALT+TAB | HOLD 800 -> TASKVIEW
主页键 = 0x7F -> TAP LALT+X | HOLD 800 -> TAP LALT+F4
```

主页键在 800ms 前松开会点按 Alt+X；达到 800ms 时立即点按一次 Alt+F4，继续按住不重复，松开不补发。

`TASKVIEW` 是系统动作：执行 `explorer.exe shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}` 打开任务视图，不注入 Win 键。电源长按使用它，避免给仍处于 HID 物理保持状态的源功能键叠加 Win 修饰键而触发 Windows 保留快捷键。

映射通过同一个全局低级键盘钩子实现；程序自身注入事件会被忽略，避免递归。

---

## 六、注意事项

- ⚠️ **遥控器需保持唤醒**：遥控器闲置会休眠，按任意键唤醒后再用语音键
- ⚠️ 一次只能连一个程序。退出 RemoteMic 后遥控器语音键才回到普通 F5 功能
- 💡 普通用户权限即可运行，无需管理员；后台用 `start.vbs`，调试用 `debug.bat`
- 💡 全局键钩子靠内部消息泵线程工作，窗口是否可见不影响功能；后台运行同样可用

---

## 七、故障排查

| 现象 | 排查 |
|------|------|
| 程序提示 remote NOT FOUND | 遥控器未连接/已休眠，按几个键唤醒它再启动 |
| 微信输入法不弹出 | 先用物理键盘按 `右Alt + 逗号` 测试：若手动也不行则是输入法设置问题；若手动能弹但遥控器不行，重启 RemoteMic |
| 弹出但转写不出文字/无反应 | 运行 `tools\CaptureCable.exe` 录制 3 秒检查 CABLE 回路是否有声音 |
| 转写的声音很小 | 正常，AGC 已自动增益；如仍太小可对着遥控器麦克风口说话 |
| 想看遥控器发了什么键 | 运行 `tools\KeySniffer.exe`，按遥控器看输出 |

**诊断命令：**
```bash
# 列出当前录音设备 + 默认设备
tools\DefDev.exe list

# 验证 CABLE 音频回路（录 3 秒）
tools\CaptureCable.exe

# 抓取所有键盘事件（看遥控器/注入发了什么）
tools\KeySniffer.exe
```

---

## 八、环境变量（可选，默认无需设置）

| 变量 | 作用 | 默认 |
|------|------|------|
| `REMOTEMIC_HOTKEY=0` | 关闭热键注入（纯音频测试用） | 开启 |
| `REMOTEMIC_DUMP=1` | 松开时把解码音频存为 `rt_dump_HHmmss.wav` | 关闭 |
| `REMOTEMIC_KEYDIAG=1` | 注入时打印前台窗口信息 | 关闭 |

---

## 九、文件说明

运行入口（根目录）：

| 文件 | 说明 |
|------|------|
| `RemoteMic.exe` | **主程序**（BLE 连接 + 解码 + 推流 + 热键 + 设备切换）；源码 `src\RemoteMic.cs` |
| `start.vbs` | **后台启动器**（无窗口常驻，日志写 `RemoteMic.log`） |
| `stop.bat` | 停止后台 RemoteMic |
| `install-autostart.bat` / `uninstall-autostart.bat` | 安装/卸载开机自启 |
| `debug.bat` | 前台启动脚本（调试看实时输出） |

源码（`src\`）与诊断工具（`tools\`）：

| 文件 | 说明 |
|------|------|
| `src\RemoteMic.cs` | 主程序源码 |
| `src\KeySniffer.cs` → `tools\KeySniffer.exe` | 诊断：全局键盘钩子，抓取所有按键事件 |
| `src\DefDev.cs` → `tools\DefDev.exe` | 诊断：列出/切换默认录音设备 |
| `src\CaptureCable.cs` → `tools\CaptureCable.exe` | 诊断：录制 CABLE Output 验证音频回路 |

文档与归档：

| 文件 | 说明 |
|------|------|
| `NOTES.md` | 完整技术笔记（协议逆向、排错历程） |
| `driver/MiRemoteHidFilter/` | F13–F19 设备专用键的 KMDF lower filter、测试签名包与安装/回滚脚本 |
| `keymap.txt` | 全局按键映射配置；RemoteMic 启动时读取 |
| `tools/HidCaps.*` | 只读 HID descriptor/preparsed metadata 验证工具 |
| `tools/RemoteKeyTest.*` | 方向上 + F13–F19 八键验收工具 |
| `_archive/` | 开发过程中的离线研究源码（归档，日常不用） |

### 重新编译
需要 .NET Framework 4.8（系统自带 csc.exe）：

```bat
build.bat
```

KeyMapper 自动测试：

```bat
tests\_run_keymap_tests.bat
tests\_run_keycombo_smoke.bat
```
