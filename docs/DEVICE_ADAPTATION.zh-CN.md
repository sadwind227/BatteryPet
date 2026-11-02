# 设备适配 · v0.4.2

面板增加「设备适配」页。当前可以完成：选程序 → 本地分析 → 导出 agent 任务 → 导入候选 → 只读试读 → 对照验证 → 启用或禁用扩展。

## 选择并分析程序

1. 点击「发现已安装程序」，选择可能控制该外设的厂商客户端。列表来自 Windows 的安装注册信息，不会启动客户端；便携程序、未记录安装位置的程序可以手动选择。
2. 也可以点击「选择程序」指定 EXE/脚本/ASAR，或点击「选择目录」指定厂商资源目录。EXE 只用来确定资源目录，不执行它。
3. 点击「分析所选程序」。结果包含电量相关片段、文件位置、证据编号、附近通信调用和连接设备的 HID 元数据。分析不会调用模型、发送硬件查询或修改厂商程序。
4. 原生二进制、加密握手或者源代码不完整时可能找不到协议；空结果会明确说明原因，不会显示“适配成功”。罗技 C547 已有内置 HID++ 适配，检测到它时会说明无需重复生成。

分析上限：最多遍历 4000 个文件，检查 1000 个脚本/配置，读取正文 32 MB；单文件 2 MB，目录深度 6，最多分析 4 个 ASAR。只读支持 ASAR 中未打包加密的文本条目，不解包执行。超限时请选择更小的资源目录。会跳过常见 agent 配置、凭据、缓存和日志目录，并遮盖常见凭据赋值；导出前仍应查看任务片段。

## 导出 agent 工作区任务

1. 从设备列表选择实际外设，或填写四位十六进制的 `VID:PID`。
2. 选择 agent，再选择一个已有工作区目录。
3. 点击「导出适配任务」。只创建 `BatteryPet-adaptation-时间-随机码` 子目录，不覆盖 AGENTS.md、CLAUDE.md 或 agent 配置。
4. 检查任务包中的 `analysis.json` 和 `TASK.md`。包内只含选定程序的有限证据片段、HID 元数据和本工具文件，不复制整个客户端、会话历史或 API key。

| 接入方式 | 操作 |
| --- | --- |
| Codex CLI | 复制生成的 PowerShell 一行命令，在终端运行；使用已安装的 `codex exec`、只读环境和输出 schema |
| Claude Code CLI | 同样运行生成的一行命令；提示经标准输入传递，关闭内置工具与 MCP 工具后分析证据 |
| DeepSeek Harness CLI | 使用已有 `dsh --profile headless`，工作目录为新任务包；需该版本支持 headless |
| Hermes CLI | 使用已有 `hermes chat --query-file ... --oneshot`；需该版本支持这些参数 |
| WorkBuddy 或其他桌面 agent | 软件生成的是一行工作区指令；在相应 agent 打开所选工作区后发送该指令。此版未假定 WorkBuddy 有统一 CLI |

一行命令由软件按实际路径生成，形如：

```powershell
& '实际 Python 路径' '任务包\tools\adapter_tool.py' run --job '任务包\job.json' --agent 'codex'
```

这行命令启动 agent 后自动接收与检查其输出，成功时保存 `candidate.json`。可以直接导入该文件，也可将另一台电脑或桌面 agent 生成的同格式 JSON 导回本机。

CLI 使用用户原有的登录、模型和额度，本程序不替用户安装 agent，也不保存或读取其登录凭据。未检测到 CLI 会返回明确错误。Windows npm `.cmd` 启动器若能识别唯一 Node 入口，会直接调用 Node，不把任务内容拼入 shell。无法识别时使用原生 CLI 或通用工作区方式。

任务包包含 `status.json`、`stdout.log`、`stderr.log`。状态可能是 running、candidate、unsupported、failed、cancelled。输出格式错误、证据编号伪造、目标设备不匹配、超时都不算成功。CLI 最多运行 10 分钟；对使用后台网关的 agent，终止 CLI 后可能还需要在其自身界面取消后台任务。

## 导入、验证和启用

1. 点击「导入候选」，选择 `candidate.json`。只接收结构固定的声明式 JSON，不加载 Python/JavaScript、DLL 或任意请求帧。
2. 点击「只读试读」，检查能否返回真实读数。此版只支持厂商专用 HID 接口（Usage Page 0xFF00–0xFFFF）的 Feature 读取和被动 Input 报告；不会发送 Feature/Output 准备命令。
3. 勾选「记录对照电量」，填写厂商客户端或其他可靠来源此刻显示的真实电量，再试读。同一设备/固件需要两个不同电量值都一致，才能点击「启用已验证适配」。重复同一个值不会增加验证覆盖。
4. 启用后按用户设置的自动刷新间隔采集，生成设备托盘与桌宠读数；禁用则停止该扩展。单个扩展读取异常在子进程内隔离，保留原有设备采集。

电量偏移从 Windows HID 报告首字节 report ID 算起，不能未经核实照搬 WebHID 的 DataView 偏移。无法证明独立读取方式，或者需要厂商握手、写入、加密协议时，agent 应生成 `unsupported` 说明，不应伪造可启用的字段。

本版最多启用 4 份扩展；每份定义读取首个匹配接口。验证记录、启用定义保存在 `%LOCALAPPDATA%\BatteryPet\adapters`，不是存放在 agent 任务包里。可复制的是候选定义和证据；导入另一台电脑后仍应在本机验证。

## 已验证与边界

- 已测试脚本与 ASAR 线索提取、敏感目录跳过、片段遮盖、任务包独立导出、命令参数传递、候选检查、不同电量对照、启停及原有罗技采集回归。
- 已用模拟 CLI 测试完整生成/导回链路，没有消耗任何真实 agent 额度。真实模型可能需要登录、权限或费用，输出质量取决于证据。
- 没有用新的未知外设完成真实电量适配，不能据此宣称所有品牌都支持；USBPcap 抓包和复杂原生协议代码适配仍是后续工作。

接入依据：[Codex 非交互入口](https://developers.openai.com/blog/codex-as-a-platform)、本机 `codex exec --help`；[Claude Code 命令行参考](https://code.claude.com/docs/en/cli-reference)；[DeepSeek Harness 官方 CLI 说明](https://github.com/deepseek-ai/deepseek-harness/blob/master/apps/cli/README.md)；[Hermes CLI](https://hermes-agent.nousresearch.com/docs/user-guide/cli)。
