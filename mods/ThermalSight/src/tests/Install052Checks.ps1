param([string]$Workspace = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $Workspace 'tmp-test\install-052'
$plugin = 'BepInEx\plugins\SprocketThermalSight'
New-Item -ItemType Directory -Path (Join-Path $fixture $plugin) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $Workspace 'data\SprocketThermalSight-0.5.1.dll') -Destination (Join-Path $fixture "$plugin\SprocketThermalSight.dll")
Copy-Item -LiteralPath (Join-Path $Workspace 'data\thermal-models-0.5.1.json') -Destination (Join-Path $fixture "$plugin\thermal-models.json")
$before = @('SprocketThermalSight.dll','thermal-models.json') | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $fixture "$plugin\$_")).Hash }
$global:Thermal052TestRunning = $true
# Test only the isolated fixture: the real game directory is never passed to either script.
function Get-Process { param([string]$Name, $ErrorAction) if ($global:Thermal052TestRunning) { [pscustomobject]@{ ProcessName = 'Sprocket' } } }
foreach ($action in @('install','uninstall')) {
    $rejected = $false
    try { & (Join-Path $Workspace "$action-0.5.2.ps1") -GameRoot $fixture | Out-Null } catch { $rejected = $_.Exception.Message -like '*Sprocket is running*'; if (!$rejected) { throw } }
    if (!$rejected) { throw "$action failed running-process refusal" }
}
$global:Thermal052TestRunning = $false
& (Join-Path $Workspace 'install-0.5.2.ps1') -GameRoot $fixture
$backup = Get-ChildItem -LiteralPath $Workspace -Directory -Filter 'backup-before-0.5.2-*' | Sort-Object Name -Descending | Select-Object -First 1
$record = Get-Content -LiteralPath (Join-Path $backup.FullName 'backup.json') -Raw | ConvertFrom-Json
if ($record.GameRoot -ne $fixture) { throw 'Wrong fixture backup root' }
foreach ($name in @('SprocketThermalSight.dll','thermal-models.json')) {
    if ((Get-FileHash -LiteralPath (Join-Path $fixture "$plugin\$name")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Workspace "package\$plugin\$name")).Hash) { throw 'Install differs from package' }
}
& (Join-Path $Workspace 'uninstall-0.5.2.ps1') -GameRoot $fixture -BackupDirectory $backup.FullName
$after = @('SprocketThermalSight.dll','thermal-models.json') | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $fixture "$plugin\$_")).Hash }
if (Compare-Object $before $after) { throw 'Uninstall did not restore exact baseline bytes' }
Add-Content -LiteralPath (Join-Path $backup.FullName "$plugin\thermal-models.json") -Value 'tamper'
$rejected = $false
try { & (Join-Path $Workspace 'uninstall-0.5.2.ps1') -GameRoot $fixture -BackupDirectory $backup.FullName | Out-Null } catch { $rejected = $_.Exception.Message -like '*Backup hash mismatch*' }
if (!$rejected) { throw 'Uninstall accepted corrupt backup' }
"PASS: install/uninstall process refusal, verified local backup, exact package hashes, exact rollback, corrupt backup rejected. Fixture=$fixture; backup=$($backup.FullName)"
