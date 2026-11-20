# Gun Sights 0.3.0: build (Release), tests, hook audit, package. Reads the game folder and the two source
# blueprints only; writes inside this folder (tmp-test, BUILD.txt, TESTS.txt, AUDIT.txt, package).
param([string]$GameDir = 'E:\SteamLibrary\steamapps\common\Sprocket')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$tmp = Join-Path $root 'tmp-test'
$fixtures = Join-Path $tmp 'fixtures'
New-Item -ItemType Directory -Force $fixtures | Out-Null
$env:TMP = $tmp; $env:TEMP = $tmp; $env:GUNSIGHTS_FIXTURES = $fixtures
$bp = Join-Path $env:USERPROFILE 'Documents\My Games\Sprocket\Factions\Nederland\Blueprints\vehicles'
foreach ($name in 'Bradley M3A3.blueprint', 'Leopard 2a7.blueprint') {
  Copy-Item -LiteralPath (Join-Path $bp $name) -Destination (Join-Path $fixtures $name) -Force
}

& dotnet build (Join-Path $root 'src\SprocketGunSights.csproj') -c Release "-p:GameDir=$GameDir" -p:UseAppHost=false *>&1 |
  Tee-Object -FilePath (Join-Path $root 'BUILD.txt') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed (BUILD.txt).' }
& dotnet build (Join-Path $root 'tests\Tests.csproj') -c Release -p:UseAppHost=false *>&1 |
  Add-Content -Path (Join-Path $root 'BUILD.txt')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed (BUILD.txt).' }
& dotnet (Join-Path $root 'tests\bin\Release\net8.0\Tests.dll') *>&1 | Tee-Object -FilePath (Join-Path $root 'TESTS.txt') | Out-Null
$testExit = $LASTEXITCODE
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Audit-Hooks.ps1') -GameDir $GameDir -OutFile (Join-Path $root 'AUDIT.json') *>&1 |
  Tee-Object -FilePath (Join-Path $root 'AUDIT.txt') | Out-Null
$auditExit = $LASTEXITCODE
if ($testExit -ne 0) { throw 'Tests failed (TESTS.txt).' }
if ($auditExit -ne 0) { throw 'Hook audit failed (AUDIT.txt).' }

$pkg = Join-Path $root 'package\BepInEx\plugins\SprocketGunSights'
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item (Join-Path $root 'src\bin\Release\net6.0\SprocketGunSights.dll') $pkg -Force
(Get-FileHash (Join-Path $pkg 'SprocketGunSights.dll') -Algorithm SHA256).Hash
