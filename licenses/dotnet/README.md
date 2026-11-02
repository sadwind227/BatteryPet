# Bundled Microsoft .NET runtime notices

BatteryPet 0.7.0-dev publishes self-contained Windows x64 binaries with Microsoft.NETCore.App.Runtime.win-x64 and Microsoft.WindowsDesktop.App.Runtime.win-x64 8.0.27. These runtimes are included; Python is not.

Runtime licenses are copied verbatim from the official Microsoft NuGet runtime packages: LICENSE.TXT, THIRD-PARTY-NOTICES.TXT and WindowsDesktop-LICENSE.txt. WPF and WinForms third-party notices are copied from their official v8.0.27 source tags:
- https://github.com/dotnet/wpf/blob/v8.0.27/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/winforms/blob/v8.0.27/THIRD-PARTY-NOTICES.TXT

Microsoft's Windows license mapping is preserved in windows-NOTICES.txt, sourced from https://github.com/dotnet/core/blob/main/license-information-windows.md (2026-11-02).
CoreCLR and certain native WindowsDesktop binaries use the .NET Library License; D3DCompiler_47_cor3.dll uses Windows SDK terms. Other runtime files use MIT subject to upstream third-party notices. Do not relabel all bundled binaries as BatteryPet's MIT code.
- https://dotnet.microsoft.com/dotnet_library_license.htm
- https://learn.microsoft.com/legal/windows-sdk/license

The release file list pins each bundled runtime asset's SHA256 to its official NuGet package bytes. The application source remains under the root MIT license. Build/audit tools and legacy Python reference tests are developer tools, excluded from the executable payload.
