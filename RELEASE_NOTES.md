# v0.6.3-dev — MIT 版本整理 / MIT release cleanup

- 移出未启用的第三方雷柏解析器及测试，主程序继续采用 MIT。
- 保留桌宠、蓝牙、设备管理及关于页功能；独立 HID 研究归档不随包分发。

# v0.6.2-dev — 关于与版本 / About and version history

- 新增“关于与版本”页，显示开发者清风、当前版本和可选择的历史更新目录。
- 修正五个导航按钮在高 DPI 下的布局。

# v0.6.1-dev — 桌宠贴边与气泡修正 / Edge placement preview

- 桌宠以实际可见内容限制位置，透明留白可越过屏幕；左右预留系统滚动条宽度，上下贴可用区域并避开任务栏。
- 自定义 PNG/GIF 的各帧可见范围及呼吸范围缓存，避免拖动时扫描源图。
- 气泡优先上方、空间不足时下方，始终留在当前显示器工作区；极小工作区等比缩小。
- 修复负坐标副屏位置恢复，支持按显示器 DPI 调整及显示配置变化后的重新约束。
- 安装版、便携版沿用原个人设置、适配协议与验证记录。无需重新进行设备适配。
- 本版为未签名开发预览版，仍需 .NET 8 Desktop Runtime x64 和 Python 3。未进行真实多显示器硬件与外部用户电脑验证。

# v0.6.0-dev — Unreleased / 未发布

- Historical experiment: an inactive GPL decoder was staged; it is excluded from v0.6.3-dev. / 历史试验曾暂存未启用解析器，v0.6.3-dev 已排除。
- Ported Rapoo battery status decoder from pinned FerrisPulse revision; not enabled as a hardware provider, not live-device verified.
- No change to immutable v0.5.0 release archives.

# BatteryPet v0.5.0 — Preview / 预览版

Bluetooth battery reads alongside existing Logitech/HID monitoring.

本版加入 BLE 标准电量读取与 Windows 蓝牙电量报告，沿用托盘、桌宠、自动刷新和提醒。

## Changes / 更新

- Added BLE Battery Service 0x180F / Battery Level 0x2A19 reads and Windows-reported Bluetooth battery values.
- Bluetooth runs in a separate helper with a 12-second timeout and progress snapshots; HID and Bluetooth failures are isolated.
- Unknown-connectivity Windows reports remain gray and initially appear only in the panel; charging is not inferred.
- 加入蓝牙 BLE 标准电量与 Windows 电量报告；两种来源去重并标记，未知在线状态不触发提醒。

- Automatic refresh every 30–3600 seconds; default 60 seconds. Changes apply immediately and are saved.
- Independently reimplemented HID discovery and report I/O using Windows APIs.
- More specific Python component startup, import and exit errors.
- Recorded idle CPU/RAM measurements, with GPU and hardware-query limits stated.
- Included .NET apphost license/notices and the maintainer's image provenance statement.
- 自动刷新可设为 30–3600 秒，默认 60 秒，立即生效并保存。
- Windows HID 通信层重写；细化读取组件错误提示。
- 补充资源占用记录、素材来源、第三方声明和逐文件 SHA-256 清单。

## Downloads / 下载哪一个

| File | Purpose / 用途 |
| --- | --- |
| `BatteryPet-v0.5.0-windows-x64-preview.zip` | Run on Windows / 分享给朋友、直接运行 |
| `BatteryPet-v0.5.0-source.zip` | Source, tests and documentation / 开发源码与文档 |
| `SHA256SUMS.txt` | Archive checksums / 压缩包校验值 |
| `RELEASE_AUDIT.json` | File inventory, hashes and packaging checks / 文件清单及打包核对结果 |

Requires Windows 10/11 x64 and .NET 8 Desktop Runtime. Python 3.10+ is needed for 2.4 GHz/HID and adapter tools; Bluetooth does not require Python. Extract the full program ZIP and run BatteryPet.exe. Runtime dependencies are not bundled.

需要上述运行环境；完整解压后运行，保留 EXE 旁边的所有文件。升级前从托盘退出旧版；换目录后请重新切换开机启动以更新路径。

## Validation and limits / 验证与边界

The built-in hardware scope remains Logitech G502 X LIGHTSPEED with receiver `046D:C547`. A previous normal-desktop test read battery percentage; the maintainer reported that the rewritten build no longer showed the component error. The automated session can enumerate the receiver but receives Windows error 5 when opening it, so successful polling is not re-certified here.

23 Python tests and 58 application checks cover offline provider behavior, adapters, preferences and rendering. These do not establish support for other hardware. Windows returned five stored Bluetooth battery reports in the automated session, with online state unconfirmed. BLE direct access was denied in that session; no live BLE device was available for comparison. Razer-specific adapters are not implemented. GPU process utilization could not be quantified. Agent CLI tests use mocks and do not consume paid model quota.

本轮硬件信息包括此前实测与维护者使用反馈，自动化环境仍受 HID 访问限制；不把错误提示消失等同于完整硬件回归。睡眠恢复、充电、多硬件、实际注销自启仍需更多测试。

Code is MIT licensed; the .NET host retains upstream notices. The sample image is included based on the maintainer's creation and redistribution-rights declaration, with separate bundle terms. The file review does not guarantee the absence of every possible copyright claim. See THIRD_PARTY_NOTICES.md and docs/COPYRIGHT_AUDIT.zh-CN.md.

