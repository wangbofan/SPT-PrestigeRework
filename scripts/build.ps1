<#
.SYNOPSIS
    Builds both the server mod and the client plugin, then packs the drag-and-drop bundle.
.EXAMPLE
    ./scripts/build.ps1 -GameDir "D:\SPT"
#>
[CmdletBinding()]
param(
    # 默认空：走 NuGet 上的 SPT 4.1.2 引用程序集，编出来的 DLL 兼容 4.1.2 及更高版本。
    # 传本地 SPT_Runtime 会把 AssemblyRef 抬高到该版本，只建议调试用。
    [string]$SptServerDir = '',
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.2'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts "SPT-PrestigeRework-$Version"

Write-Host 'Building server mod...'
$srvArgs = @((Join-Path $root 'src\PrestigeRework\PrestigeRework.csproj'), '-c', $Configuration, '-v', 'm')
if ($SptServerDir) { $srvArgs += "-p:SptServerDir=$SptServerDir" }
& dotnet build @srvArgs
if ($LASTEXITCODE -ne 0) { throw 'server build failed' }

Write-Host 'Building client plugin...'
& dotnet build (Join-Path $root 'src\PrestigeSkipTransfer\PrestigeSkipTransfer.csproj') -c $Configuration "-p:GameDir=$GameDir" -v m
if ($LASTEXITCODE -ne 0) { throw 'plugin build failed' }

$srvOut = Join-Path $root "src\PrestigeRework\bin\$Configuration"
$cliOut = Join-Path $root "src\PrestigeSkipTransfer\bin\$Configuration"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$modDir = Join-Path $stage 'SPT_Runtime\user\mods\PrestigeRework'
$pluginDir = Join-Path $stage 'BepInEx\plugins\PrestigeSkipTransfer'
New-Item -ItemType Directory -Force -Path (Join-Path $modDir 'config'), $pluginDir | Out-Null

Copy-Item (Join-Path $srvOut 'PrestigeRework.dll') $modDir
if (Test-Path (Join-Path $srvOut 'PrestigeRework.pdb')) { Copy-Item (Join-Path $srvOut 'PrestigeRework.pdb') $modDir }
Copy-Item (Join-Path $srvOut 'Config\config.json') (Join-Path $modDir 'config')
Copy-Item (Join-Path $root 'README.md') $modDir

Copy-Item (Join-Path $cliOut 'PrestigeSkipTransfer.dll') $pluginDir
if (Test-Path (Join-Path $cliOut 'PrestigeSkipTransfer.pdb')) { Copy-Item (Join-Path $cliOut 'PrestigeSkipTransfer.pdb') $pluginDir }
Copy-Item (Join-Path $root 'README.md') $pluginDir

$zip = Join-Path $artifacts "SPT-PrestigeRework-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host ''
Write-Host "OK -> $zip"
