# BatteryPet v0.4.1 — Preview / 预览版

First proposed GitHub release, collecting the local 0.1.0–0.4.1 development milestones. Not yet published; pending the third-party permission check described in THIRD_PARTY_NOTICES.md.

首次拟发布的 GitHub 版本，汇总本地 0.1.0–0.4.1 开发成果。尚未发布，完整包公开前须处理第三方声明中的授权事项。

## Highlights / 本版功能

- Logitech G502 X LIGHTSPEED battery reads through C547 2.4G receiver. / 罗技 C547 接收器电量读取。
- Large pixel tray digits, colored battery state and configurable low-battery alerts. / 大号像素托盘数字及低电量提醒。
- Interactive whale, click-only battery bubble, custom images/GIFs and click sounds. / 互动鲸鱼、自定义图片、GIF 和音效。
- Optional sign-in startup and silent tray mode. / 可选自启动和静默启动。
- Local vendor-client evidence analysis and agent-assisted adapter workflow. / 厂商程序分析及 agent 辅助适配流程。
- Fixed dragging outside the screen work area. / 修复拖动越过桌面边界。

## Requirements / 环境

Windows 10/11 x64, .NET 8 Desktop Runtime, Python 3.10+. Extract the entire archive before running BatteryPet.exe. Runtime dependencies are not bundled; do not copy the EXE alone.

需要 Windows 10/11 x64、.NET 8 Desktop Runtime、Python 3.10+。完整解压运行，不是单文件独立程序，也未捆绑运行环境。

## Validation and limits / 验证与限制

- 23 Python tests and 44 application checks passed; real battery percentage read from G502 X LIGHTSPEED.
- Bluetooth and other built-in receiver families are not implemented/verified. Complex encrypted/handshake protocols are not auto-generated.
- Agent integrations have offline/mock CLI tests, not live paid model verification. Automatic logon startup has not been tested by logging out the user.
- Imported adapters need two different battery-level comparisons before activation.

23 项 Python、44 项程序内检查通过；尚未覆盖完整硬件矩阵、实际注销登录和真实付费 agent。详细见 CHANGELOG.md 与验证记录。
