> 本文保留的是 v0.5.0 发布包的历史审计，不适用于 v0.6.0-dev。新版已经引入 GPL 解析模块，见 [许可说明](LICENSING.zh-CN.md)；旧版结论不构成新版审计。

# v0.5.0 文件与权利检查

检查日期：2026-11-02。本记录覆盖本次生成的程序 ZIP 与源码 ZIP，逐文件散列和许可类别见发布目录的 `RELEASE_AUDIT.json` 与包内 `MANIFEST.sha256`。它不覆盖旧 REVIEW ZIP、旧上传副本或 GitHub 历史提交。

## 结论与证据边界

在已检查的发布树中，未发现仍需沿用旧 HID 文件未确认授权、或混入参考项目美术和应用源码的已知阻碍。已保留 .NET 启动器的上游许可；示例图片依据维护者的制作与分发权声明纳入。

这不是“全文件绝无版权问题”的法律保证。程序扫描不能证明所有代码原创，也不能独立证明未指明原角色的公有领域状态。图片权利目前依据维护者于开发会话中的声明，不能将这项证据写成独立确认。

## 逐类检查

| 文件/组件 | 来源与许可依据 | 本版处理 |
| --- | --- | --- |
| 根目录 C# 应用代码与检查代码 | BatteryPet 开发会话中实现，MIT 声明 | 纳入；保留文件头与根许可证 |
| Python 采集、适配和测试 | 项目实现；仅导入标准库与本地模块 | 纳入；解析 import 并阻止未审核依赖 |
| `hid_win.py` | 基于微软公开 Windows API 重新编写 | 纳入新版；排除旧文件指纹 |
| `read_logitech.py` | 项目中的 HID++ 查询实现；特性编号和布局为协议通信事实 | 纳入；此前有 C547/G502 X 实测记录，不据此声称全型号兼容 |
| BluetoothBatteryProvider.cs、BluetoothChecks.cs | 项目依据 Windows 原生接口独立实现；没有引入蓝牙库或其他应用源码 | 纳入 MIT；系统接口依据见蓝牙说明 |
| 文档、打包与校验脚本 | 项目制作，MIT | 纳入；相对链接与发布清单核对 |
| `docs/images/*.png` | 本项目渲染器及界面检查生成的预览 | 纳入；设备数值注明模拟 |
| `Examples/CustomPet/batterypet-pet.png` | 维护者声明角色属公有领域、图片由其制作且持有公开分发权 | 纳入；单独记录来源及随包分发范围，不改授 MIT |
| `BatteryPet.dll`、JSON 输出 | 从本版清单内源码构建 | 纳入；依赖表仅含 BatteryPet 项目 |
| `BatteryPet.exe` | .NET Windows apphost 8.0.27 | 随附原始 MIT 与上游第三方声明 |
| 系统 DLL、字体、.NET 与 Python 运行环境 | 本机已安装系统或运行环境 | 不复制进发布包 |
| FerrisPulse / EZ2.4G / INCOTT / DeepSeek Widget / Coopanion | 研究和交互参考 | 不捆绑其应用源码、音频或美术 |

示例图 SHA-256：`cbaba4d89387e8759cd492d02651b0efd26ae08221d25d6af3e2fc1257cd9abd`。它的完整声明在 [SOURCE.md](../Examples/CustomPet/SOURCE.md)。

## 执行过的检查

1. 枚举发布清单内所有文件，标记代码、素材、生成输出和上游许可；不从旧发布目录递归收集。
2. 检查源文件中的作者、许可、来源与外部依赖线索。当前工程没有 NuGet PackageReference，Python 没有第三方包依赖；发布后的依赖 JSON 也再次核对。
3. 将当前 HID/罗技通信和图标文件，与原上游的 `hid_win.py`、`battery.py`、`win_icon.py` 进行去空白的连续五行完全匹配筛查，结果均为零。该筛查只能帮助发现残留复制，不是原创性的证明。
4. 将旧通信文件 SHA-256 `599950a8d7d66230fa93b151efcf610871ea0a73829627944bae3da8b68d3a3a` 加入拒绝清单；同时固定已说明来源的图片指纹。
5. 从干净源码副本构建并运行测试，程序包排除 PDB、缓存、个人配置、日志、任务包及旧 ZIP。
6. 对两个 ZIP 逐项核对名称、完整清单、文件散列、ZIP CRC、文档链接和许可文件；输出每个文件的记录。

检查脚本与清单位于源码包的 `scripts/release_audit.py` 和 `scripts/release-files.json`。清单变更意味着必须重新检查新增来源，不应只更新散列绕过检查。

## 上游许可依据

- [.NET Windows 许可说明](https://github.com/dotnet/core/blob/main/license-information-windows.md)将普通 apphost 列在 MIT 文件范围；本包不含该页列出的特殊 Windows 运行时二进制。
- 随包保留 [runtime v8.0.27 LICENSE](https://github.com/dotnet/runtime/blob/v8.0.27/LICENSE.TXT) 和 [第三方声明](https://github.com/dotnet/runtime/blob/v8.0.27/THIRD-PARTY-NOTICES.TXT)，原文见 [licenses/dotnet](../licenses/dotnet/README.md)。
- Windows HID 接口依据 [Microsoft HID API](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-api)；没有将微软文档页面或示例整篇复制进包。

若未来新增第三方源码、字体、音效或角色，需先补充相应授权范围与声明。已有本地旧包不因本次检查自动获得许可。
