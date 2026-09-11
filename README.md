# RemoteMapper

把小米蓝牙语音遥控器变成 Windows 的**语音输入话筒**和**可编程遥控器**。

按住遥控器语音键 → 微信输入法语音录入自动弹出，遥控器麦克风的声音实时送进去；松开 → 结束录入并恢复原来的默认麦克风。其余按键可以在可视化面板里改成任意快捷键，躺着也能操作电脑。

纯 C# 单文件程序，普通用户权限运行，不联网。

---

## 能做什么

- **语音直通** —— 解码蓝牙 BLE ATVV 协议里的 IMA ADPCM 音频，经 VB-Cable 实时送进输入法，延迟无感
- **用完即还** —— 只在按住语音键期间临时切换默认录音设备，松开立刻恢复，平时开会录音不受影响
- **可视化按键映射** —— 托盘双击打开映射面板，单击 / 双击 / 长按 / 连发四种手势，保存即热加载，无需重启
- **动作不止快捷键** —— 组合键、任务视图、启动程序、执行命令，甚至跑一段 C# 表达式把结果输入到光标处
- **设备级按键隔离** —— 配套 KMDF lower filter 把这只遥控器的按键改写到 F13–F20，物理键盘上的同名键完全不受牵连

## 选哪个分支

| | `main`（当前） | `driverless-keymap` |
|---|---|---|
| 驱动 | 需要 HID filter 驱动 + 开启测试签名 | 不需要，拷贝即用 |
| 可映射按键 | 全部（含返回、音量 ±） | 除返回、音量 ± 外的 9 个键 |
| 按键来源隔离 | 是，物理键盘不受影响 | 否，映射的键会连带物理键盘同名键 |
| 适合 | 自己长期使用 | 交付给别人 |

要在新机器上部署 `main`，见 [`DEPLOY.md`](DEPLOY.md)。

---

## 快速开始

1. **装 [VB-Audio Virtual Cable](https://vb-audio.com/Cable/)**（免费），装完系统会多出 `CABLE Input` / `CABLE Output` 一对虚拟设备，不用手动改默认录音设备。
2. **装 [微信输入法](https://z.weixin.qq.com/)**，在设置里开启语音输入的「按住说话」，并把它的快捷键设成和程序的语音热键一致（如 `右 Alt + 逗号`）。
3. **配对遥控器**：设置 → 蓝牙 → 添加「小米蓝牙语音遥控器」。配对后它同时是 BLE 设备（语音）和 HID 键盘（按键）。
4. **双击 `start.vbs`** 后台常驻，日志写进 `RemoteMic.log`；想看实时输出就用 `debug.bat`。

看到这几行就绪即可使用：

```text
== RemoteMic: remote mic -> CABLE + WeChat IME hotkey ==
[1/4] connecting to remote... OK (MI RC)
[2/4] setting up ATVV service... OK
[3/4] opening VB-Cable Input... OK
[4/4] ATVV handshake... ready
>> HOLD the voice button to talk. Release to stop.
```

把光标点进任意文本框，按住语音键说话，松开结束。

退出：托盘图标右键「退出」，或双击 `stop.bat`。开机自启：`install-autostart.bat`（卸载用 `uninstall-autostart.bat`）。

---

## 按键映射

双击托盘图标打开映射面板（独立窗口），点按键卡片改单击 / 双击 / 长按的动作，保存后写回 `keymap.json` 并立即生效。

出厂映射：

```text
电源  短按 Alt+Tab    长按 任务视图
返回  短按 Ctrl+Z     长按 Ctrl+Shift+Z
主页  短按 Alt+X      长按 Alt+F4
音量+ Backspace       音量- Delete       直播 Esc
```

方向键和确定键保持原样，语音键固定用于说话。

也可以直接编辑 `keymap.json`：

```json
{
  "enabled": true,
  "keys": [
    {
      "id": "power", "name": "电源键", "vk": "0x82",
      "click": { "kind": "combo", "tap": true, "keys": "LALT+TAB" },
      "hold":  { "kind": "taskview", "ms": 600 }
    }
  ]
}
```

| `kind` | 作用 | 额外字段 |
|---|---|---|
| `combo` | 发送组合键 | `keys`，如 `LCTRL+SHIFT+Z` |
| `taskview` | 打开任务视图 | — |
| `launch` | 启动程序 | `command` |
| `cmd` | 执行命令 | `command` |
| `code` | 运行 C# 表达式并输入返回值 | `command`，如 `DateTime.Now.ToString("HH:mm")` |

`tap: true` 表示等源键抬起后原子点按一次；不写则在源键按住期间保持目标组合。长按用 `ms` 设阈值，达到阈值立刻触发一次，继续按住不重复、松开不补发。

---

## 工作原理

```text
按住语音键 ──┬─> BLE 通知 CTL「按下」
             │     ├─ 默认录音设备切到 CABLE Output（让输入法录得到）
             │     ├─ 注入语音热键按下 → 输入法弹出
             │     └─ 解码遥控器音频并推流到 CABLE Input
             │
             └─> HID 同时发出 F5 → 被键盘钩子吞掉，不干扰

松开语音键 ──┬─> 停止推流 → 释放热键 → 恢复原默认录音设备
```

触发信号完全走 BLE 通道，不依赖那颗会污染热键组合的 F5。全局映射走同一个低级键盘钩子，程序自己注入的事件会被忽略，不会递归。

---

## HID 设备专用键隔离（可选）

遥控器把音量加、音量减、返回放在 HID Keyboard Page 的 `0x80/0x81/0xF1` usage 上，`kbdhid.sys` 不会为它们生成按键事件。[`driver/MiRemoteHidFilter`](driver/MiRemoteHidFilter/README.md) 在 `kbdhid` 解析前等长改写为 F13/F14/F15，顺带把主页、菜单、直播、电源改成 F16–F19、语音键改成 F20。于是全局映射只需监听 F13–F20，物理键盘上的 Home / Apps / 反引号 / Power / F5 保持原样。

HVCI 可以保持开启；当前包用 WDK 测试证书签名，需要 TESTSIGNING。安装与回滚步骤见驱动目录的 README。

---

## 环境变量

| 变量 | 作用 |
|---|---|
| `REMOTEMIC_HOTKEY=0` | 关闭热键注入（纯音频测试） |
| `REMOTEMIC_DUMP=1` | 松开时把解码音频存成 `rt_dump_HHmmss.wav` |
| `REMOTEMIC_KEYDIAG=1` | 注入时打印前台窗口信息 |

## 故障排查

| 现象 | 处理 |
|---|---|
| `remote NOT FOUND` | 遥控器休眠了，按几个键唤醒再启动 |
| `ATVV service NOT FOUND` | Windows 缓存了旧的 GATT 表：在蓝牙设置里删除该遥控器并重新配对 |
| 输入法不弹出 | 先用物理键盘按同一个热键试；能弹说明是程序侧，重启 RemoteMic |
| 转写不出文字 | `tools\CaptureCable.exe` 录 3 秒，确认 CABLE 回路有声音 |
| 想知道遥控器发了什么键 | `tools\KeySniffer.exe` |

## 项目结构

| 路径 | 说明 |
|---|---|
| `src\RemoteMic.cs` | 主程序：BLE 连接、音频解码推流、热键注入、设备切换 |
| `src\KeyMap*.cs`、`src\KeyComboSender.cs`、`src\KeySnippet.cs` | 按键映射引擎与托盘面板服务 |
| `ui\keymap.html`、`ui\remote.png`、`ui\app.ico` | 映射面板前端与图标 |
| `driver\MiRemoteHidFilter\` | KMDF lower filter 源码、签名包、安装/回滚脚本 |
| `tools\` | KeySniffer / DefDev / CaptureCable 等诊断工具 |
| `NOTES.md` | 协议逆向与排错过程的完整技术笔记 |
| `_archive\` | 开发期的离线研究代码 |

### 重新编译

需要 .NET Framework 4.8（系统自带 `csc.exe`）：

```bat
build.bat
tests\_run_keymap_tests.bat
tests\_run_keycombo_smoke.bat
```

---

## 许可证

[MIT](LICENSE)
