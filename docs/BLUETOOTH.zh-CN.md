# 蓝牙电量 · v0.5.0

先在 Windows 中配对并连接外设，然后打开 BatteryPet 或点击「刷新电量」。支持的读数会出现在设备面板，沿用自动刷新间隔、托盘图标样式、桌宠选择和低电量提醒。

## 支持两种来源

| 来源 | 条件 | 显示和提醒 |
| --- | --- | --- |
| BLE 标准电量服务直接读取 | 已配对设备提供 Battery Service 0x180F 和可读 Battery Level 0x2A19 | 标明「蓝牙 BLE」「标准电量服务直接读取」；成功后作为当前读数 |
| Windows 蓝牙电量报告 | 驱动向 Windows 提供百分比 | 标明「Windows 报告电量」；未确认在线时标灰，不触发低电量提醒 |

Windows 报告可能来自缓存，没有可用的设备采样时间。「检查于」只表示本程序查询 Windows 的时间。已配对和设备节点存在，都不等于设备此刻在线。

新发现且未确认在线的报告值只默认列在面板中；若希望保留其托盘图标，可手动开启该设备的「显示独立托盘图标」。读数相同的不同设备按物理身份区分，同一设备的 Windows 报告和 BLE 直接读数合并，优先使用直接读数。

## 范围与限制

- BLE 鼠标、键盘等是否支持，取决于设备是否开放标准电量特性。部分耳机也可通过 Windows 报告提供电量。
- 经典蓝牙耳机的厂商协议、每只耳塞与充电盒的独立读数、Apple 等私有扩展不在本版实现范围。
- 标准 Battery Level 只提供百分比，不会据此猜测充电状态。
- 断开、休眠、权限限制或不提供电量时，不伪造当前值。旧值会标记；请连接、唤醒后重试。
- 本版不扫描陌生设备、不配对、不改蓝牙开关，也不发送设置写入。GATT 读取可能建立与已配对设备的连接。
- 蓝牙查询在独立进程中运行，最多约 12 秒；超时保留已取得的结果。每轮最多尝试 16 个 BLE 设备。与 HID 采集互相隔离，退出时取消查询。
- 蓝牙部分不依赖 Python；罗技 2.4G/HID 与适配工具仍需要 Python。

## 验证状态

当前电脑的原生调用链已取得 5 份 Windows 保存的蓝牙电量报告，均未确认在线，按缓存值处理。自动化会话的 BLE 接口打开受到 Windows 错误 5 限制。维护者当前没有可用的在线蓝牙外设进行对照，因此**未完成成功 BLE 直读的实机验证**。

离线检查覆盖 0/100 与非法百分比、原生结构布局、短/长 UUID、同名设备身份、缓存与直读去重、接口回退、部分失败保留、来源故障隔离及退出取消。模拟检查不能代替设备实测。

## 接口依据

本版依据 Windows 公开接口声明独立实现，未加入第三方蓝牙库或其他应用源码：

- [Bluetooth 设备与接口 GUID](https://learn.microsoft.com/en-us/windows/win32/api/_bltooth/)
- [BluetoothGATTGetServices](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothleapis/nf-bluetoothleapis-bluetoothgattgetservices)
- [BluetoothGATTGetCharacteristics](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothleapis/nf-bluetoothleapis-bluetoothgattgetcharacteristics)
- [BluetoothGATTGetCharacteristicValue](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothleapis/nf-bluetoothleapis-bluetoothgattgetcharacteristicvalue)
- [System.Devices.BatteryLife](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-batterylife)

另尝试 Windows 蓝牙驱动可选属性 `{104EA319-6EE2-4701-BD47-8DDBF425BBE5},2`。它并非稳定的公开接口契约；未提供该属性时跳过。该属性的兼容性线索来自 Microsoft Q&A 的[蓝牙电量报告讨论](https://learn.microsoft.com/en-us/answers/questions/1156194/bluetooth-device-battery-indicator-display-in-syst)，未复制其中脚本。
