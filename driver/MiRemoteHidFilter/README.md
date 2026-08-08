# MiRemoteHidFilter

设备专属 KMDF lower filter，用于修复小米蓝牙语音遥控器 2 Pro 被 `kbdhid.sys` 丢弃的三个 HID Keyboard Page usage，并为当前需要全局映射的普通遥控器键分配设备专用 F 键。

## 绑定范围

过滤器通过 extension INF 精确绑定：

- VID：`0x2717`
- PID：`0x32B8`
- Hardware ID：`HID\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&012717_PID&32b8_REV&00a4`

它不会匹配其他物理键盘。

## 映射

| 物理键 | 原始 HID usage | 改写为 | Windows VK |
|---|---:|---:|---:|
| 音量加 | `0x80` | F13 `0x68` | `0x7C` |
| 音量减 | `0x81` | F14 `0x69` | `0x7D` |
| 返回 | `0xF1` | F15 `0x6A` | `0x7E` |
| 主页 | `0x4A` | F16 `0x6B` | `0x7F` |
| 菜单 | `0x65` | F17 `0x6C` | `0x80` |
| 直播 | `0x35` | F18 `0x6D` | `0x81` |
| 电源 | `0x66` | F19 `0x6E` | `0x82` |

前三个 usage 原本会被 `kbdhid.sys` 丢弃；后四个本可映射为 Home / Apps / OEM_3 / Power，但全局低级键盘钩子没有来源设备 ID，直接映射会误吞物理键盘的同名键。因此把它们改为 F16–F19，仅由此 VID/PID 的遥控器生成。

未映射的确定与方向键保持原样，保留其原生按住/重复行为。若未来要为这些普通键增加组合键映射，必须先在此 filter 分配未使用的 F20–F24，再在 `keymap.txt` 使用对应 VK；不要直接把 `VK_ENTER` / 方向 VK 设为映射源。

## 报告格式与实现

实机 preparsed metadata：

- Top-level collection：Generic Desktop / Keyboard (`0x0001/0x0006`)
- `InputReportByteLength = 121`
- Keyboard Report ID：`0x01`
- Vendor Report ID：`0x06/0x07/0x08`，不修改

内核实测按键报告：

```text
01 00 00 <usage> 00 ...
```

因此：

```text
report[0] = Report ID
report[1] = modifiers
report[2] = reserved
report[3] = pressed-key usage
```

过滤器位于：

```text
kbdclass -> kbdhid -> MiRemoteHidFilter -> mshidumdf
```

它转发 `IRP_MJ_READ`，在下层完成后原地、等长修改 Report ID `0x01` 的 `report[3]`，随后把原状态和传输长度返回给 `kbdhid`。不修改 Report Descriptor、Report ID 或报告长度。

## 实机验收

Windows 11 x64、HVCI/内存完整性开启时通过：

```text
方向上 = VK_UP 0x26  PASS
音量加 = F13 0x7C   PASS
音量减 = F14 0x7D   PASS
返回键 = F15 0x7E   PASS
主页键 = F16 0x7F   PASS
菜单键 = F17 0x80   PASS
直播键 = F18 0x81   PASS
电源键 = F19 0x82   PASS
```

验证工具：

```bat
verify-keys.bat
```

## 目录

```text
MiRemoteHidFilter/
├── driver.c / driver.h       KMDF filter
├── remap.c / remap.h         纯报告改写逻辑
├── MiRemoteHidFilter.inf     设备专属 extension INF
├── MiRemoteHidFilter.vcxproj WDK 项目
├── build.bat                 Debug/Release 构建
├── package/                  当前可安装的测试签名包
├── prepare-test-mode.*       信任证书并启用 TESTSIGNING
├── install-driver.*          安装 package/
├── uninstall-driver.*        卸载所有旧版本包
├── restore-normal-mode.*     卸载后关闭 TESTSIGNING、移除证书
└── verify-keys.bat           八键验收
```

## 构建

要求：

- Visual Studio 2022 Build Tools，含 C++ x64 与对应 Spectre libraries
- WDK 10.0.26100
- Visual Studio 组件 `Windows Driver Kit Build Tools`

构建 Release，并自动刷新 `package/`：

```bat
build.bat Release
```

项目配置：KMDF 1.15、`/W4 /WX`。最终版本已通过 PREfast DriverMinimumRules、InfVerif、ApiValidator 和 Inf2Cat。

## 安装测试签名包

> `package/` 是 WDK 测试证书签名的开发包。它需要 Windows TESTSIGNING；关闭测试模式后不能继续加载。

1. 确认 Secure Boot 已关闭。
2. 管理员运行：

   ```bat
   prepare-test-mode.bat
   ```

3. 重启 Windows。
4. 管理员运行：

   ```bat
   install-driver.bat
   ```

5. 若 Windows 更新了正在运行的驱动映像，再重启一次。
6. 运行 `verify-keys.bat`。

HVCI/内存完整性可以保持开启。

## 回滚与退出测试模式

先卸载过滤器：

```bat
uninstall-driver.bat
```

重启并确认遥控器键盘栈恢复后，再执行：

```bat
restore-normal-mode.bat
```

然后再次重启。不要在过滤器仍安装时直接关闭 TESTSIGNING，否则 Windows 会拒绝加载测试签名内核驱动。

若正常启动异常，可进入安全模式卸载对应 extension INF。

## 正式发布限制

要在关闭 TESTSIGNING 的正常代码完整性模式下继续使用 KMDF 版本，需要 Microsoft Hardware Dev Center attestation/WHCP 签名。UMDF 2 迁移仅作为后续研究方向，目前未实现。
