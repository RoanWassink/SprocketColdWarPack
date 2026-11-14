<#
.SYNOPSIS
    Builds Sprocket Visual Effects and stages the files for the game folder, in the layout of the game folder.
.DESCRIPTION
    Writes nothing into the game: -Out must be a new or empty folder, and -GameDir is only read (BepInEx core/interop
    DLLs to build against). Installing is done separately (dist\install-all.ps1 of the repack).
    Result below -Out:
      BepInEx\plugins\SprocketVisualEffects\SprocketVisualEffects.dll, sprocketvfx.bundle, assets\, lamp-assets\
      BepInEx\config\sprocket.visualeffects.cfg
      Sprocket_Data\StreamingAssets\Parts\vfxLamp*Part.json
      Sprocket_Data\StreamingAssets\Localization\en-UK\Parts\*.xml
.EXAMPLE
    .\Build-Package.ps1 -Out C:\temp\vfx-stage -GameDir <folder with BepInEx\core and BepInEx\interop>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket'
)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($Out)
if((Test-Path -LiteralPath $out) -and @(Get-ChildItem -LiteralPath $out -Force).Count -gt 0){throw "-Out must be empty: $out"}
$bundle=Join-Path $PSScriptRoot 'sprocketvfx.bundle'
if(-not (Test-Path -LiteralPath $bundle -PathType Leaf) -or (Get-Item -LiteralPath $bundle).Length -eq 0){throw "AssetBundle missing or empty: $bundle"}
$build=Join-Path ([IO.Path]::GetTempPath()) ('sprocketvfx-build-'+[Guid]::NewGuid().ToString('N'))
Push-Location $PSScriptRoot
try{
    if((dotnet --version) -ne '8.0.425'){throw 'Build requires the pinned SDK 8.0.425 (global.json).'}
    dotnet build SprocketVisualEffects.csproj -c Release -o $build --nologo "-p:GameDir=$GameDir" -p:ContinuousIntegrationBuild=true
    if($LASTEXITCODE){throw 'Build failed.'}
    $plugin=Join-Path $out 'BepInEx\plugins\SprocketVisualEffects'
    $config=Join-Path $out 'BepInEx\config'
    $parts=Join-Path $out 'Sprocket_Data\StreamingAssets\Parts'
    $locale=Join-Path $out 'Sprocket_Data\StreamingAssets\Localization\en-UK\Parts'
    New-Item -ItemType Directory -Path (Join-Path $plugin 'assets'),(Join-Path $plugin 'lamp-assets'),$config,$parts,$locale -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $build 'SprocketVisualEffects.dll') -Destination $plugin
    Copy-Item -LiteralPath $bundle -Destination $plugin
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') -File | Copy-Item -Destination (Join-Path $plugin 'assets')
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'lamp-assets') -File | Copy-Item -Destination (Join-Path $plugin 'lamp-assets')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sprocket.visualeffects.cfg') -Destination $config
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'parts') -Filter '*.json' | Copy-Item -Destination $parts
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'localization') -Filter '*.xml' | Copy-Item -Destination $locale
}finally{
    Pop-Location
    if(Test-Path -LiteralPath $build){Remove-Item -LiteralPath $build -Recurse -Force}
}
Get-ChildItem -LiteralPath $out -File -Recurse | ForEach-Object {
    [pscustomobject]@{File=$_.FullName.Substring($out.TrimEnd('\').Length+1);Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
} | Format-Table -AutoSize
