# Installs SprocketThermalSight 0.5.5 to E:\SteamLibrary\steamapps\common\Sprocket with backup and SHA256 verification.
# Refuses to run while Sprocket is open.
param([string]$GameDir = "E:\SteamLibrary\steamapps\common\Sprocket")
$ErrorActionPreference = 'Stop'

if (Get-Process -Name "Sprocket" -ErrorAction SilentlyContinue) {
    throw "Sprocket is currently running. Close Sprocket first, then re-run this script."
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pkg = Join-Path $root "package"
$expectedDllHash = "428784028225359B06683EB745B3C0F979E85650F9B9FEA3F9F139BC64FC8102"

$srcDll = Join-Path $pkg "BepInEx\plugins\SprocketThermalSight\SprocketThermalSight.dll"
$actualSrcHash = (Get-FileHash $srcDll -Algorithm SHA256).Hash
if ($actualSrcHash -ne $expectedDllHash) {
    throw "Package DLL hash mismatch: expected $expectedDllHash, got $actualSrcHash"
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $root "backup-before-0.5.5-$stamp"
$dstPlugin = Join-Path $GameDir "BepInEx\plugins\SprocketThermalSight"
$dstAssets = Join-Path $dstPlugin "assets"
$dstParts = Join-Path $GameDir "Sprocket_Data\StreamingAssets\Parts"
$dstLoc = Join-Path $GameDir "Sprocket_Data\StreamingAssets\Localization\en-UK\Parts"

New-Item -ItemType Directory -Force -Path $backup, $dstAssets, $dstParts, $dstLoc | Out-Null
if (Test-Path (Join-Path $dstPlugin "SprocketThermalSight.dll")) {
    Copy-Item (Join-Path $dstPlugin "SprocketThermalSight.dll") (Join-Path $backup "SprocketThermalSight.dll") -Force
}
if (Test-Path (Join-Path $dstPlugin "thermal-models.json")) {
    Copy-Item (Join-Path $dstPlugin "thermal-models.json") (Join-Path $backup "thermal-models.json") -Force
}

Copy-Item $srcDll (Join-Path $dstPlugin "SprocketThermalSight.dll") -Force
Copy-Item (Join-Path $pkg "BepInEx\plugins\SprocketThermalSight\thermal-models.json") (Join-Path $dstPlugin "thermal-models.json") -Force
foreach ($f in @("driver-console.obj", "driver-console-icon.png", "fcs-display-icon.png", "commander-sight.obj", "commander-sight-icon.png", "commander-console-icon.png")) {
    Copy-Item (Join-Path $pkg "BepInEx\plugins\SprocketThermalSight\assets\$f") (Join-Path $dstAssets $f) -Force
}
foreach ($f in @("driverConsolePart.json", "fcsDisplayConsolePart.json", "commanderConsolePart.json", "commanderPanoramicSightPart.json")) {
    Copy-Item (Join-Path $pkg "Sprocket_Data\StreamingAssets\Parts\$f") (Join-Path $dstParts $f) -Force
}
foreach ($f in @("driverConsole.xml", "fcsDisplayConsole.xml", "commanderConsole.xml", "commanderPanoramicSight.xml")) {
    Copy-Item (Join-Path $pkg "Sprocket_Data\StreamingAssets\Localization\en-UK\Parts\$f") (Join-Path $dstLoc $f) -Force
}

$installedHash = (Get-FileHash (Join-Path $dstPlugin "SprocketThermalSight.dll") -Algorithm SHA256).Hash
if ($installedHash -ne $expectedDllHash) {
    throw "Installed DLL hash mismatch!"
}
Write-Host "Installed SprocketThermalSight 0.5.5 cleanly. DLL SHA256=$installedHash"
Write-Host "Backup saved at: $backup"
