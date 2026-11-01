# Changelog / 版本记录

These are local development milestones, not previously published GitHub releases. The first proposed release is **v0.4.1 (Preview)**; it has not been published.

以下为本地开发里程碑，不代表已发布的 GitHub Release。首次建议发布 **v0.4.1 预览版**，目前尚未发布，不为历史版本伪造提交或标签。

## [0.4.1] - 2026-11-02

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
