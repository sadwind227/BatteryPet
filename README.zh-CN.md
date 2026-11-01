# BatteryPet · 电量桌宠

Windows 外设电量托盘工具与互动桌宠，支持像素电量图标、自定义角色与音效，以及通过 AI Agent 工作区辅助生成设备适配。

[English](README.md) · [版本记录](CHANGELOG.md) · [详细操作](docs/USER_GUIDE.zh-CN.md) · [设备适配](docs/DEVICE_ADAPTATION.zh-CN.md)

**当前版本：0.4.1 · 预览版**

> 发布准备状态：HID 通信文件的第三方再分发许可尚待确认。MIT 不覆盖该文件或第三方图片，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。不要将整包宣传为已完成许可审核的 MIT 软件。

![角色配色和点击反馈](docs/images/pet-themes.png)

## 功能

- 设备独立托盘图标：加大像素数字、圆环、电池样式，电量变色与左键刷新。
- 点击桌宠才弹电量气泡；支持拖动、实时桌面边界限制、跨屏、缩放、三种配色、动画、置顶和显隐。
- 导入 PNG/JPG/GIF/BMP 图片、WAV/MP3 点击音效；可试听、恢复默认并自动保存。
- 自定义低电量提醒并去重；读取失败标注旧数据，不伪造 0%。
- 可选登录 Windows 自启动与仅托盘静默启动；自启动和点击音效默认关闭。
- 厂商程序本地分析、agent 工作区任务导出、JSON 候选导入和只读验证。

## 支持范围

| 设备 | 连接 | 状态 |
| --- | --- | --- |
| 罗技 G502 X LIGHTSPEED，接收器 `046D:C547` | 2.4G | 已实机读取百分比 |
| 其他罗技型号/接收器 | 2.4G | 尚未验证，不保证支持 |
| 声明式 HID Feature/Input 扩展 | 厂商 HID 接口 | 实验性，须本地验证 |
| 蓝牙外设 | 蓝牙 | 尚未实现 |

内置罗技采集查询槽位 1、2 的 HID++ `0x1004` 特性。休眠、充电、拔插与 G HUB 共存仍需更多实机测试。

## 下载和启动

需要 **Windows 10/11 x64、.NET 8 Desktop Runtime、Python 3.10+**，无需第三方 Python 库。

Release 发布后，到 [下载页](https://github.com/sadwind227/BatteryPet/releases) 下载 Windows ZIP，完整解压并运行 `BatteryPet.exe`。本版未捆绑运行环境，不是单文件程序；请保留旁边所有脚本和配置，不要只复制 EXE。

关闭面板继续常驻，退出用托盘菜单。静默启动后从托盘恢复桌宠。[详细操作](docs/USER_GUIDE.zh-CN.md)

## 设备适配

在「设备适配」页选择程序、分析、选择 VID/PID 和 agent 工作区、导出任务。Codex、Claude Code、DeepSeek Harness、Hermes 提供 CLI 模板；WorkBuddy 等桌面 agent 使用一行工作区指令。对应 agent 需要自行安装和登录，运行可能消耗其额度。

导回 `candidate.json`，在两个不同真实电量值下与可靠来源对照一致后才能启用。当前只支持无需准备命令的 Feature 读取和被动 Input；不保证自动逆向任意程序，也不自动重放未知命令。复杂握手、加密和 USB 抓包向导尚未实现。[完整说明](docs/DEVICE_ADAPTATION.zh-CN.md)

## 构建与验证

```powershell
dotnet build -c Release
python -m unittest test_provider.py test_adaptation.py -v
dotnet publish -c Release --no-self-contained -o artifacts/app
```

程序参数：`--demo` 为独立配置的模拟演示；`--ui-check`、`--adapt-check`、`--icon-check` 分别生成界面、适配链路和图标检查记录。模拟数据不计入硬件支持。

已通过 **23 项 Python 测试、44 项程序内检查**，未调用真实 agent 测试付费接口，未覆盖完整硬件场景。[验证记录](docs/TESTING.zh-CN.md)

## 数据与许可

配置、素材、读取诊断和验证记录在 `%LOCALAPPDATA%\BatteryPet`。日常监测不联网；运行外部 agent 时，其服务可能收到任务内容，发送前请查看任务包。

本项目自有代码使用 [MIT](LICENSE)；第三方代码和图片有独立范围，请阅读 [第三方声明](THIRD_PARTY_NOTICES.md) 与 [设计参考](docs/DESIGN_REFERENCES.zh-CN.md)。
