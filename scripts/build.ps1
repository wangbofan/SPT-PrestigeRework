<#
.SYNOPSIS
    Builds both the server mod and the client plugin, then packs the drag-and-drop bundle.
.EXAMPLE
    ./scripts/build.ps1 -SptServerDir "D:\SPT\SPT_Runtime" -GameDir "D:\SPT"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SptServerDir,
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts "SPT-PrestigeRework-$Version"

Write-Host 'Building server mod...'
& dotnet build (Join-Path $root 'src\PrestigeRework\PrestigeRework.csproj') -c $Configuration "-p:SptServerDir=$SptServerDir" -v m
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
