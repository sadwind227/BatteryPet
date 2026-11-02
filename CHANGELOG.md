# v0.6.3-dev — MIT 版本整理 / MIT release cleanup

- 移出未启用的第三方雷柏解析器及测试，主程序继续采用 MIT。
- 保留桌宠、蓝牙、设备管理及关于页功能；独立 HID 研究归档不随包分发。

# v0.6.2-dev — 关于与版本 / About and version history

- 新增“关于与版本”页，显示开发者清风、当前版本和可选择的历史更新目录。
- 修正五个导航按钮在高 DPI 下的布局。

# v0.6.1-dev — 桌宠贴边与气泡修正 / Edge placement preview

- 新增“关于与版本”页：开发者清风、当前版本及可选择的历史更新目录。
- 设备列表支持隐藏、恢复、手动排序与拖动位置提示；精简设备卡片文案并修复底部按钮裁切。
- 设备适配页分步说明，刷新间隔数字显示修正。
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

# Changelog / 版本记录

These are local development milestones. The current prepared release is **v0.5.0 (Preview)**; preparing files does not publish a GitHub release.

以下为本地开发里程碑，不代表已发布的 GitHub Release。本次整理 **v0.5.0 预览版**；生成文件不等于已上传发布。

## [0.5.0] - 2026-11-02

- Add native BLE Battery Service reads and optional Windows Bluetooth battery reports. / 加入原生 BLE 标准电量读取和 Windows 蓝牙电量报告。
- Deduplicate by physical identity; mark cached values with unknown connectivity and avoid alerts for them. / 按物理身份去重；在线状态未知的报告标灰，不触发提醒。
- Keep Bluetooth and HID failures isolated, with bounded Bluetooth helper runtime and partial results. / 隔离蓝牙与 HID 故障，限制蓝牙查询时间，保留已取得结果。
- Show the actual connection and source in device cards; new uncertain devices initially stay in the panel. / 显示实际连接方式和来源，未确认在线的新设备先列在面板中。
- Bluetooth uses only .NET and Windows APIs; no extra package or Python dependency for Bluetooth. / 蓝牙不引入第三方包且不依赖 Python。
- Validation: 23 Python and 58 application checks; Windows report retrieval verified, live BLE direct read not hardware-verified. / 23 项 Python、58 项程序内检查；已验证系统报告读取，成功 BLE 直读尚待实机对照。

## [0.4.2] - 2026-11-02

- Reimplemented Windows HID transport using documented system APIs; receiver access still requires normal desktop validation. / 根据 Windows 官方 API 独立重写 HID 通信层，接收器仍需正常桌面实测。
- Show specific Python component failures instead of a generic exception. / 细化 Python 读取组件错误提示。
- Let users set automatic refresh to 30–3600 seconds; apply and save immediately. / 自动刷新可设为 30–3600 秒，立即生效并保存。
- Recorded controlled CPU and memory measurements in `docs/PERFORMANCE.zh-CN.md`. / 记录受控 CPU 与内存测量。
- Added bilingual release documentation, a reviewed package file list, source/program ZIPs and per-file checksums. / 整理双语说明、打包白名单、程序与源码 ZIP 和逐文件校验。
- Preserved .NET apphost notices and recorded maintainer-created image provenance. / 补齐 .NET 启动器声明与维护者自制图片来源。

## [0.4.1] - 2026-11-02

### Changed / 改进
- Replaced the optional third-party character preview with a maintainer-supplied sample pet. / 将可选第三方角色预览替换为维护者提供的示例桌宠。

### Fixed / 修复
- Constrain movement while dragging, including negative-coordinate monitors. / 拖动中实时限制工作区边界，兼容负坐标副屏。
- Correct and save position after interrupted mouse capture. / 拖动中断时校正并保存位置。
- Validation: 23 Python tests and 44 application checks passed. / 23 项 Python、44 项程序内检查通过。

## [0.4.0] - 2026-11-02

### Added / 新增
- Installed-program discovery and bounded script/ASAR analysis. / 程序发现和本地脚本、ASAR 分析。
- Agent workspace export, CLI command templates and desktop-agent instructions. / agent 工作区导出、命令和任务指令。
- Declarative adapter import, read-only trials, comparison verification and enable/disable. / 声明式适配导入、试读、验证和启停。
- Isolated extension reads with timeouts. / 子进程隔离和读取超时。

### Fixed / 修复
- Python lookup fallback when registry access fails. / Python 注册信息访问失败时回退。

## [0.3.1] - 2026-11-02

### Changed / 改进
- Larger native-size pixel digits with dark outlines. / 数字放大、描边及按实际托盘尺寸绘制。
- Full-height compact `100`; unobstructed charging marker. / 100 等高紧凑显示，充电标记避开笔画。

## [0.3.0] - 2026-11-02

### Added / 新增
- Optional sign-in startup and independent silent startup. / 可选自启动和静默启动。
- Custom PNG/JPG/GIF/BMP pets and WAV/MP3 click sounds. / 自定义图片、GIF、点击音效。
- Preview, restore defaults and managed local asset copies. / 试听、恢复默认及素材独立保存。

## [0.2.0] - 2026-11-02

### Added / 新增
- Independently drawn whale, colors and idle/touch/drag animation. / 自绘鲸鱼、配色和互动。
- Click-only battery bubble and scale/duration/topmost settings. / 点击气泡及缩放、时长、置顶设置。
- Redesigned device and appearance panels. / 设备与外观面板美化。

## [0.1.0] - 2026-11-02

### Added / 新增
- Initial local prototype: Logitech C547 HID++ reads, tray icons, alerts and a basic pet. / 首个本地原型：罗技读取、托盘、提醒和基础桌宠。
- Real-hardware battery read from G502 X LIGHTSPEED. / 已实机读取 G502 X LIGHTSPEED 电量。

