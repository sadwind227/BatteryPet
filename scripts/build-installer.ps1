# SPDX-License-Identifier: MIT
param([string]$Version = '0.6.3-dev')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$release = Join-Path $root "artifacts/releases/v$Version"
$payload = Join-Path $release "BatteryPet-v$Version-windows-x64-preview.zip"
if (!(Test-Path -LiteralPath $payload)) { & (Join-Path $PSScriptRoot 'package.ps1') -Version $Version }
if ($Version -ne '0.6.3-dev') { throw 'Update the installer display version before building another version.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$installer = Join-Path $release "BatteryPet-v$Version-Setup.exe"
if (Test-Path -LiteralPath $installer) { throw "Refusing to overwrite $installer" }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$installer" /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$payload,payload.zip" (Join-Path $PSScriptRoot 'installer/Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$testRoot = Join-Path $root ('artifacts/installer-test-' + [guid]::NewGuid().ToString('N'))
$test = Start-Process -FilePath $installer -ArgumentList @('--self-test', ('"' + $testRoot + '"')) -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $testRoot 'PASS.txt'))) { throw "Installer self-test failed: $testRoot" }
Copy-Item -LiteralPath (Join-Path $testRoot 'PASS.txt') -Destination (Join-Path $release 'INSTALLER_CHECKS.txt')
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
Add-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Value "$hash  $([IO.Path]::GetFileName($installer))" -Encoding utf8
Write-Output "Installer prepared: $installer"

