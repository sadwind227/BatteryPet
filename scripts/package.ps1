param([string]$Version = '0.4.1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $projectRoot 'BatteryPet.csproj'
$declared = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if ($Version -ne $declared) { throw "Version does not match BatteryPet.csproj: $declared" }
$work = Join-Path $projectRoot ('artifacts/build-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'BatteryPet'
dotnet publish $project -c Release -r win-x64 --no-self-contained -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Get-ChildItem -LiteralPath $app -Filter '*.pdb' | Remove-Item
foreach ($file in @('README.md','README.zh-CN.md','CHANGELOG.md','LICENSE','THIRD_PARTY_NOTICES.md','RELEASE_NOTES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $app
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $app -Recurse
$output = Join-Path $projectRoot ('artifacts/releases/v' + $Version)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$zip = Join-Path $output ('BatteryPet-v' + $Version + '-windows-x64-preview-REVIEW.zip')
if (Test-Path -LiteralPath $zip) { throw "Archive already exists; choose a new version or preserve the previous archive first: $zip" }
Compress-Archive -LiteralPath $app -DestinationPath $zip
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
($hash.Hash.ToLowerInvariant() + '  ' + (Split-Path $zip -Leaf)) | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Local review archive: $zip"
Write-Warning 'Third-party HID redistribution permission is pending. This archive is not approved for public release merely because it builds.'
