<#
.SYNOPSIS
    Stages the files of this mod in the folder layout of a Sprocket folder. Never touches a game folder.
.DESCRIPTION
    Copies the five files this mod ships to <Out>\BepInEx\... and <Out>\Sprocket_Data\... (same relative paths as in the
    Sprocket folder). Run it after a Release build. <Out> must be empty or not exist. Installing is done by the
    release's own install script, not by this mod.
.PARAMETER Out
    Target folder for the staged layout (for example a scratch folder).
.PARAMETER Dll
    Built DLL (default: bin\Release\net6.0\SprocketCupolas.dll next to this script).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$Dll
)
$ErrorActionPreference='Stop'
if(-not $Dll){$Dll=Join-Path $PSScriptRoot 'bin/Release/net6.0/SprocketCupolas.dll'}
$mapping=[ordered]@{
 'BepInEx/plugins/SprocketCupolas/SprocketCupolas.dll'=$Dll
 'BepInEx/plugins/SprocketCupolas/t90a-cupola-icon.png'=(Join-Path $PSScriptRoot 'Assets/approved/t90a-cupola-icon.png')
 'Sprocket_Data/StreamingAssets/Parts/t90aCupolaPart.json'=(Join-Path $PSScriptRoot 'parts/t90aCupolaPart.json')
 'Sprocket_Data/StreamingAssets/Localization/en-UK/Parts/t90aCupola.xml'=(Join-Path $PSScriptRoot 'localization/t90aCupola.xml')
 'BepInEx/config/sprocket.cupolas.cfg'=(Join-Path $PSScriptRoot 'sprocket.cupolas.cfg')
}
if((Test-Path -LiteralPath $Out) -and @(Get-ChildItem -LiteralPath $Out -Force).Count -gt 0){throw "Out folder is not empty: $Out"}
$count=0
foreach($relative in $mapping.Keys){
 $source=$mapping[$relative];$dest=Join-Path $Out $relative
 if(-not (Test-Path -LiteralPath $source -PathType Leaf)){throw "Missing source file: $source"}
 New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force | Out-Null
 Copy-Item -LiteralPath $source -Destination $dest -Force
 $count++
}
$actual=@(Get-ChildItem -LiteralPath $Out -File -Recurse)
if($actual.Count -ne $mapping.Count){throw 'Unexpected file in staged layout.'}
Write-Output "Staged $count files in $Out (own DLL, icon, part, localization, default config; no dependency/interop binaries)"
