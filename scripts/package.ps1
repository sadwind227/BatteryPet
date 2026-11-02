# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
param([string]$Version = '0.6.3-dev')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$configPath = Join-Path $PSScriptRoot 'release-files.json'
$files = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$declared = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'BatteryPet.csproj') -Raw -Encoding UTF8)).Project.PropertyGroup.Version
if ($Version -ne $declared -or $Version -ne $files.version) { throw 'Project, file list and requested versions must match.' }
$output = Join-Path $projectRoot ('artifacts/releases/v' + $Version)
foreach ($suffix in @('source','windows-x64-preview')) {
    $archive = Join-Path $output ("BatteryPet-v$Version-$suffix.zip")
    if (Test-Path -LiteralPath $archive) { throw "Refusing to overwrite release: $archive" }
}
$source = Join-Path $output 'source/BatteryPet'
$work = Join-Path $projectRoot ('artifacts/pkg-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$buildSource = Join-Path $work 'build-source'
$app = Join-Path $output 'windows/BatteryPet'
foreach ($folder in @($source,$buildSource,$app)) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}

# Copy only reviewed files, never a whole working directory.
foreach ($name in $files.source_files) {
    $original = Join-Path $projectRoot $name
    if (!(Test-Path -LiteralPath $original -PathType Leaf)) { throw "Missing reviewed source file: $name" }
    foreach ($destination in @($source,$buildSource)) {
        $target = Join-Path $destination $name
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $original -Destination $target
    }
}
$audit = Join-Path $buildSource 'scripts/release_audit.py'
$stagedConfig = Join-Path $buildSource 'scripts/release-files.json'
& python $audit --config $stagedConfig folder --kind source --root $source
if ($LASTEXITCODE -ne 0) { throw 'Source file review failed' }
Push-Location $buildSource
try {
    & python -m unittest test_provider.py test_adaptation.py
    if ($LASTEXITCODE -ne 0) { throw 'Python checks failed' }
}
finally { Pop-Location }
& dotnet publish (Join-Path $buildSource 'BatteryPet.csproj') -c Release -r win-x64 --no-self-contained -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$pdb = Join-Path $app 'BatteryPet.pdb'
if (Test-Path -LiteralPath $pdb) { Remove-Item -LiteralPath $pdb }
foreach ($name in $files.document_files) {
    $target = Join-Path $app $name
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $target -Force
}
& python $audit --config $stagedConfig folder --kind windows --root $app
if ($LASTEXITCODE -ne 0) { throw 'Published file review failed' }

# Run checks in a separate copy so their fixtures cannot enter either ZIP.
$checkApp = Join-Path $work 'check-app'
foreach ($name in @($files.program_files) + @($files.document_files)) {
    $target = Join-Path $checkApp $name
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $app $name) -Destination $target
}
& dotnet (Join-Path $checkApp 'BatteryPet.dll') --ui-check
if ($LASTEXITCODE -ne 0) { throw 'Application check process failed' }
$checkName = 'ui-check/' + [char]0x7ed3 + [char]0x679c + '.txt'
$checkReport = Get-Content -LiteralPath (Join-Path $checkApp $checkName) -Encoding UTF8
if ($checkReport -match 'FAIL') { throw ('Application checks failed: ' + ($checkReport | Select-Object -Last 1)) }
$passed = @($checkReport | Where-Object { $_ -like 'PASS*' }).Count
if ($passed -lt 58) { throw 'Incomplete application check report' }
$checkReport | Set-Content -LiteralPath (Join-Path $output 'APPLICATION_CHECKS.txt') -Encoding utf8

& python $audit --config $stagedConfig pack --source $source --app $app --output $output
if ($LASTEXITCODE -ne 0) { throw 'Archive verification failed' }
Copy-Item -LiteralPath (Join-Path $source 'RELEASE_NOTES.md') -Destination $output
Write-Output "Prepared v$Version : $output"
Write-Output "Python tests: 23; application checks: $passed. No GitHub upload was performed."

