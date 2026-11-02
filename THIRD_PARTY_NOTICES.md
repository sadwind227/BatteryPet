# Licenses and third-party notices / 许可与第三方声明

BatteryPet application source uses [MIT](LICENSE). Third-party notices and sample-image terms remain separate.

BatteryPet 程序源码采用 MIT；第三方声明和素材授权单独保留。

## Windows executable host

`BatteryPet.exe` contains the Microsoft .NET apphost from `Microsoft.NETCore.App.Host.win-x64 8.0.27`. The framework-dependent package does not bundle the .NET runtime or Python. The apphost's original MIT license and upstream notices are included under [licenses/dotnet](licenses/dotnet/README.md). Windows system libraries and fonts are referenced through OS APIs, not copied into this package.

Windows 包含 .NET apphost 启动器，已保留对应 MIT 许可证与上游声明；未捆绑 .NET/Python 运行环境、Windows DLL 或字体文件。

## Maintainer-created sample image

`Examples/CustomPet/batterypet-pet.png` was supplied and created by the BatteryPet maintainer. On 2026-11-02, the maintainer stated that the depicted character is in the public domain and confirmed public redistribution rights. The maintainer requested its inclusion in BatteryPet source and Windows packages.

该图由维护者制作并提供；维护者于 2026-11-02 声明角色属于公有领域，确认持有图片公开分发权，并授权随 BatteryPet 源码和程序包分发。此项依据是维护者的来源与权利声明，并非对角色公有领域状态的独立法律核验。

The image is authorized for inclusion and redistribution with BatteryPet. No broader standalone image license or MIT relicensing is asserted. [Provenance and scope](Examples/CustomPet/SOURCE.md).

## HID transport and references

The current `hid_win.py` was reimplemented for BatteryPet from the documented Windows SetupAPI, HID and overlapped I/O interfaces. The previous upstream transport with unconfirmed redistribution permission is excluded from v0.4.2 packages. Interface names and constants are used to call Windows APIs.

当前 HID 通信层依据微软公开接口重新编写；旧版来源授权不明的通信文件已从 v0.4.2 发布清单排除。旧本地归档不能代替本版包。

Bluetooth discovery, Windows properties and standard BLE reads in v0.5.0 are also implemented through system APIs without third-party Bluetooth code or packages. API references and compatibility-property limits are recorded in [Bluetooth notes](docs/BLUETOOTH.zh-CN.md).

- API references: [Finding and Opening a HID Collection](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/finding-and-opening-a-hid-collection), [HID API](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hid-api).
- [FerrisPulse](https://github.com/Ferris-echo/FerrisPulse) was a research reference. Its unused decoder is excluded from this version.
- [EZ2.4G](https://github.com/Ap0LoG1zE/EZ2.4G) and [INCOTT Driver](https://github.com/oexi/incott-driver-desktop) were research references; their application code is not included.
- [DeepSeek Balance Whale Widget](https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget) and [Coopanion](https://github.com/Pal-AI-Lab/Coopanion) informed interaction research; their artwork, audio and application source are not included.

The default whale, tray glyphs and soft click are produced by BatteryPet's renderer and tone generator. Product names and marks belong to their respective owners; no affiliation is implied.

默认鲸鱼、托盘数字和轻触音由本项目代码生成。产品名称与商标属于相应权利人，不表示官方关联。
