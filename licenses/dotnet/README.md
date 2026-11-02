# .NET executable host notices

The Windows framework-dependent BatteryPet.exe uses the apphost from Microsoft.NETCore.App.Host.win-x64 8.0.27. The .NET runtime itself is not bundled.

The accompanying license and third-party notices are copied verbatim from the corresponding runtime source tag:

- https://github.com/dotnet/runtime/blob/v8.0.27/LICENSE.TXT
- https://github.com/dotnet/runtime/blob/v8.0.27/THIRD-PARTY-NOTICES.TXT

Microsoft's Windows license information identifies the ordinary apphost among the files covered by MIT; the special Windows runtime binaries it lists are not part of this package:

- https://github.com/dotnet/core/blob/main/license-information-windows.md
- https://github.com/dotnet/runtime/blob/main/docs/project/licensing-assets.md

If a future build uses another apphost version or embeds the runtime, recheck these notices against that distribution.
