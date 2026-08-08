# 小米遥控器：无自定义 Windows 驱动替代方案简析

调研日期：2026-08-07

## 结论

如果同时要求：

1. 继续使用小米蓝牙语音遥控器 2 Pro；
2. 返回、音量加、音量减三个原本不产生 Windows 扫描码/VK 的键全部可用；
3. 保留现有 ATVV 语音链路；
4. 全局低延迟、稳定工作；

那么目前的设备专属 KMDF HID lower filter 仍是总复杂度最低的纯软件方案。没有找到同等能力、真正纯用户态且更轻的受支持 Windows API 路线。

## 为什么常见用户态工具无效

Windows 会将 Keyboard/Keypad top-level collection 作为系统输入设备独占打开。用户态键盘工具通常工作在 `kbdhid` 已经生成扫描码/VK 之后。本设备的三个目标 usage 没有进入该下游事件流，因此以下工具没有可映射的输入：

- 注册表 `Scancode Map`
- PowerToys Keyboard Manager
- AutoHotkey
- `WH_KEYBOARD_LL`
- Raw Input 的键盘事件
- `WM_APPCOMMAND`

Win32 HID/WinRT HID 也不能作为普通第二客户端读取系统独占的 keyboard collection。Microsoft 的 WinRT HID 文档还明确把 Keyboard Page `0x07` 全部列为 inaccessible usage。

## 方案比较

| 路线 | 三键可用 | 保留语音 | Windows 自定义驱动 | 综合判断 |
|---|---:|---:|---:|---|
| 现有精确绑定 KMDF lower filter | 是 | 是，原链路不变 | 需要 | **当前最优** |
| 只映射 Windows 已可见的九个键 | 否 | 是 | 不需要 | 唯一真正轻量的纯软件妥协 |
| PowerToys/AHK/Raw Input/Scancode Map | 否 | 是 | 不需要 | 无法替代 |
| Win32/WinRT HID 或 WebHID 直接读键盘报告 | 否 | 不适用 | 不需要 | 系统独占/受保护，无法作为可靠路线 |
| WinRT GATT 直接订阅 HOGP Report characteristic | 理论上可能 | 需要额外处理 | 不需要 | Windows 对“系统 HOGP 与应用并行读取”没有受支持保证；本机探索也未成功，不建议 |
| ETW/HCI 实时抓包再注入 | 实验上可能 | 是 | 不需要 | 诊断接口，不是稳定输入 API；高权限、脆弱、延迟和版本兼容性差 |
| Android/Linux/树莓派网络中继 | 是 | 语音需另行转发 | 不需要 | 若不要语音可行；保留语音后明显更重 |
| ESP32-S3/nRF52/RPi BLE→USB HID 代理 | 是 | 需要代理/解码 ATVV | 不需要 | 无 Windows 驱动，但增加硬件和固件；总体比当前 filter 重 |
| 修改遥控器固件/Report Map | 是 | 可保留 | 不需要 | 技术上最干净，但消费设备通常无公开刷写入口，风险最高 |
| 更换发送标准 HID usage 的遥控器 | 是 | 取决于新设备 | 不需要 | **运维上最轻**，但不能保证同样的语音体验 |
| 第三方商业蓝牙栈/重映射驱动 | 可能 | 不确定 | 仍安装第三方驱动 | 不是真正无驱动，影响面反而更大 |

## 两个值得保留的备选结论

### 1. 如果可以牺牲三个物理键

卸载 filter，仅保留 RemoteMic 的全局 KeyMapper，映射 Windows 已经能识别的九个键。这是唯一比现状真正轻的纯软件方案，但不能恢复返回和音量加减。

### 2. 如果必须完全退出 Windows 测试签名模式

优先级建议：

1. 为现有 KMDF 包做 Microsoft 正式签名；
2. 更换 HID 实现规范的遥控器/2.4 GHz USB 接收器设备；
3. 最后才考虑 BLE-to-USB 硬件代理。

硬件代理只是把驱动复杂度搬到外部固件，并不会让“完整按键 + ATVV 语音”本身变简单。

## 为什么当前 filter 的位置合理

当前 filter 精确绑定 VID/PID/REV，只在 Report ID `0x01` 中等长替换 usage：音量±/返回改为 F13-F15，且把需要全局组合键映射的主页/菜单/直播/电源改为 F16-F19，其他报告原样通过。后四项隔离避免 `WH_KEYBOARD_LL` 缺少来源设备 ID 时误吞物理键盘的 Home/Apps/反引号/Power。它正好位于原始 HID report 已到达、但 `kbdhid` 尚未丢弃目标 usage 的唯一稳定 seam；ATVV vendor reports 不受修改。

## 一手资料

- Microsoft HID Architecture（Keyboard/Keypad collection 的 exclusive access）：  
  https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-architecture
- Microsoft Keyboard and Mouse HID Client Drivers（系统独占打开键鼠 collection）：  
  https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-hid-client-drivers
- Microsoft Top-Level Collections Opened by Windows for System Use：  
  https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/top-level-collections-opened-by-windows-for-system-use
- Microsoft Windows.Devices.HumanInterfaceDevice（Inaccessible Usages）：  
  https://learn.microsoft.com/en-us/uwp/api/windows.devices.humaninterfacedevice
- Microsoft Raw Input Overview：  
  https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input
- Microsoft LowLevelKeyboardProc：  
  https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
- Microsoft Scan Code Mapper for Keyboards：  
  https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-class-drivers#scan-code-mapper-for-keyboards
- Bluetooth SIG HID over GATT Profile：  
  https://www.bluetooth.com/specifications/specs/hid-over-gatt-profile-1-0/
- Microsoft WinRT GATT API：  
  https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile
- WebHID specification（protected usages）：  
  https://wicg.github.io/webhid/
- Chromium HID protected-usage implementation：  
  https://source.chromium.org/chromium/chromium/src/+/main:services/device/public/cpp/hid/hid_report_utils.cc
- USB-IF HID Usage Tables：  
  https://usb.org/document-library/hid-usage-tables
