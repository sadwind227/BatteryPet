# 许可迁移与源码复用

自 0.6.0-dev 起，BatteryPet 的组合发行采用 GPLv3。现有自有 MIT 文件保留其原声明，原 MIT 全文见 [BatteryPet-MIT.txt](../licenses/BatteryPet-MIT.txt)。组合许可不撤销旧版已授予的 MIT 权利。旧版发布 ZIP 不修改。

GPL 全文见 [LICENSE](../LICENSE)。分发二进制时，应同时提供该版本完整对应源码与必要构建材料，保留版权、许可和修改说明。项目的源码 ZIP 和 Windows ZIP 应成对发布；仓库首页不能替代发行版对应源码。GPL 允许收费分发，但不得剥夺接收者按许可修改和再分发的权利。

第一项复用为 `rapoo_protocol.py`，来源、固定提交与修改内容见 [FerrisPulse 记录](../licenses/ferrispulse/README.md)。它只解析状态，不执行硬件 I/O，尚未启用为 provider，合成测试不等同于雷柏真机验证。原始 MIT 第三方声明也一并保留。

第三方 .NET apphost 与示例图片仍按各自声明处理，根许可证不自动更改其许可。旧 v0.5.0 版权审计仅覆盖旧包，不是本开发版的审计结果。

本开发版未发布到 GitHub，未宣称全品牌适配或 EAC 兼容认证。后续实际接入设备查询时继续记录每项源文件、依赖、修改及真机验证范围。
