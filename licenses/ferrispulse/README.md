# FerrisPulse reuse / 源码复用记录

Upstream: https://github.com/Ferris-echo/FerrisPulse

Revision: `4fcda58d8166d32423dc1175f0110b567f728975` (2026-10-31).

License: GPLv3; full text is in [GPL-3.0.txt](../GPL-3.0.txt).

`rapoo_protocol.py` ports the status decoding logic of [RapooProtocol.cs](https://github.com/Ferris-echo/FerrisPulse/blob/4fcda58d8166d32423dc1175f0110b567f728975/src/Hardware/RapooProtocol.cs). Upstream credits rapoo-tray, Copyright (c) 2026 Iris, MIT; its original notice is preserved in [rapoo-tray-MIT.md](rapoo-tray-MIT.md).

Modifications by sadwind227 for BatteryPet, 2026-11-02: Python port, fixed 19-byte length, reject invalid percentage, return only battery/charging fields. This module performs no hardware I/O and is not yet enabled as a device provider. Synthetic tests do not establish hardware compatibility.

适配仅移植电量相关状态解析，未引入 DPI 设置、宏、游戏检测或动态插件。暂无雷柏真机验证，不宣称新增设备已经适配。后续每项移植都需追加文件、提交、改动及许可记录。
