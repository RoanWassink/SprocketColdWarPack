# Runs every check of the mast once, in a scratch copy of the tree. Never writes in the source tree or the game.
#
#   powershell -NoProfile -File tools\Run-Gate.ps1 -WorkDir <empty scratch folder> [-GameDir <game, read only>] [-GameRef <folder with the Keybinds and UI Kit DLLs>]
#
#  -GameDir  Sprocket game folder. READ ONLY: Mono.Cecil, interop, GameAssembly.dll hash, installed part definitions, Cpp2IL dump.
#  -GameRef  folder laid out like the game (BepInEx\core, BepInEx\interop, BepInEx\unity-libs, BepInEx\plugins\SprocketKeybinds,
#            BepInEx\plugins\SprocketUiKit); the mast is built against it. Default: GameDir.
#  -WorkDir  scratch folder; the tree is copied to <WorkDir>\run, all steps run there, the log is <WorkDir>\gate.log.
#  -KitCoreDir  Sprocket UI Kit Core sources for tests\KitRepro (default: ..\ui-kit\Core; the step is skipped when absent).
# The shared Sprocket Keybinds and UI Kit DLLs are referenced, never copied into the mast.
param(
 [Parameter(Mandatory=$true)][string]$WorkDir,
 [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
 [string]$GameRef='',
 [string]$KitCoreDir=''
)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($GameRef)){ $GameRef=$GameDir }
$source=Split-Path $PSScriptRoot -Parent
$WorkDir=[IO.Path]::GetFullPath($WorkDir)
$GameDir=[IO.Path]::GetFullPath($GameDir); $GameRef=[IO.Path]::GetFullPath($GameRef)
if($WorkDir.StartsWith($GameDir,[StringComparison]::OrdinalIgnoreCase)){ throw 'WorkDir must not be inside the game folder.' }
if($WorkDir.StartsWith($source,[StringComparison]::OrdinalIgnoreCase)){ throw 'WorkDir must not be inside the source tree.' }
$run=Join-Path $WorkDir 'run'
if(Test-Path -LiteralPath $run){ Remove-Item -LiteralPath $run -Recurse -Force }
New-Item -ItemType Directory -Force -Path $run | Out-Null
$logFile=Join-Path $WorkDir 'gate.log'
Set-Content -LiteralPath $logFile -Value ('Run-Gate ' + (Get-Date -Format 's')) -Encoding UTF8

# Copy the tree without build output and logs.
& robocopy $source $run /E /XD bin obj logs out /XF '*.log' /NFL /NDL /NJH /NJS /NP | Out-Null
if($LASTEXITCODE -ge 8){ throw "robocopy failed ($LASTEXITCODE)" }
New-Item -ItemType Directory -Force -Path (Join-Path $run 'logs') | Out-Null

Push-Location $run
$script:steps=0
function Step([string]$name,[scriptblock]$action){
 $script:steps++
 $ErrorActionPreference='Continue'   # Windows PowerShell 5.1 turns native stderr lines into errors under 'Stop'
 $global:LASTEXITCODE=0
 $text = (& $action 2>&1 | ForEach-Object { "$_" } | Out-String)
 $code = $LASTEXITCODE
 Add-Content -LiteralPath $logFile -Value ("=== $name`r`n" + $text) -Encoding UTF8
 if($code -ne 0){ Write-Host "FAIL $name (exit $code)"; Write-Host $text; throw "$name failed with exit code $code" }
 Write-Host "PASS $name"
}
try {
 $dll=Join-Path $run 'out\SprocketTelescopicMast.dll'
 Step 'build mast (Release, against GameRef)' { dotnet build SprocketTelescopicMast.csproj -c Release -o out "-p:GameDir=$GameRef" -p:UseSharedCompilation=false }
 Step 'rotator mesh data is reproducible (stl_to_mesh --check)' { py tools\stl_to_mesh.py --check }
 Step 'rotator mesh acceptance (test_rotator_mesh)' { py tools\test_rotator_mesh.py }
 Step 'build and run Mast.Tests' {
  dotnet build tests\Mast.Tests.csproj -c Release -p:UseSharedCompilation=false
  if($LASTEXITCODE -eq 0){ dotnet tests\bin\Release\net8.0\Mast.Tests.dll }
 }
 Step 'Verify.ps1 (hook signatures, game hash, parts, API signatures, kit, icons)' { powershell -NoProfile -File Verify.ps1 -GameDir $GameDir -GameRef $GameRef -DllPath $dll }
 Step 'VerifyKeybinds.ps1' { powershell -NoProfile -File VerifyKeybinds.ps1 -GameDir $GameDir -GameRef $GameRef -DllPath $dll }
 Step 'Check-Hooks.ps1' { powershell -NoProfile -File tools\Check-Hooks.ps1 -DllPath $dll -GameDir $GameDir }
 Step 'Verify-Flows.ps1' { powershell -NoProfile -File tools\Verify-Flows.ps1 -DllPath $dll -GameDir $GameDir }
 Step 'Verify-Panel.ps1' { powershell -NoProfile -File tools\Verify-Panel.ps1 -DllPath $dll -GameDir $GameDir -GameRef $GameRef }
 Step 'Check-HookRvas.ps1 (reads the Cpp2IL dump in the game folder)' { powershell -NoProfile -File tools\Check-HookRvas.ps1 -GameDir $GameDir }
 Step 'icon checker regression (icon-src\test-flood.ps1)' { powershell -NoProfile -File icon-src\test-flood.ps1 }
 # Reproduction projects: production method bodies against doubles of the native boundary.
 Step 'twin repro: generate, build, run' {
  py tools\Generate-TwinRepro.py
  if($LASTEXITCODE -eq 0){ dotnet build tests\TwinRepro\TwinRepro.csproj -c Release -p:UseSharedCompilation=false }
  if($LASTEXITCODE -eq 0){ dotnet tests\TwinRepro\bin\Release\net8.0\TwinRepro.dll }
 }
 Step 'play repro: generate, build, run' {
  py tools\Generate-PlayRepro.py
  if($LASTEXITCODE -eq 0){ dotnet build tests\PlayRepro\PlayRepro.csproj -c Release -p:UseSharedCompilation=false }
  if($LASTEXITCODE -eq 0){ dotnet tests\PlayRepro\bin\Release\net8.0\PlayRepro.dll }
 }
 Step 'discovery repro: generate, build, run' {
  py tools\Generate-DiscoveryRepro.py
  if($LASTEXITCODE -eq 0){ dotnet build tests\DiscoveryRepro\DiscoveryRepro.csproj -c Release -p:UseSharedCompilation=false }
  if($LASTEXITCODE -eq 0){ dotnet tests\DiscoveryRepro\bin\Release\net8.0\DiscoveryRepro.dll }
 }
 Step 'mesh audit: build, record baseline of this tree, audit it' {
  dotnet build tests\MeshAudit\MeshAudit.csproj -c Release -p:UseSharedCompilation=false
  if($LASTEXITCODE -eq 0){ dotnet tests\MeshAudit\bin\Release\net8.0\MeshAudit.dll --baseline }
  if($LASTEXITCODE -eq 0){ dotnet tests\MeshAudit\bin\Release\net8.0\MeshAudit.dll }
 }
 # The kit repro compiles the UI Kit's own Core sources; they are not part of this mod (default: ..\ui-kit\Core next to this tree).
 if([string]::IsNullOrWhiteSpace($KitCoreDir)){ $KitCoreDir=Join-Path (Split-Path $source -Parent) 'ui-kit\Core' }
 if(Test-Path -LiteralPath $KitCoreDir){
  Step 'kit repro: build, run' {
   dotnet build tests\KitRepro\KitRepro.csproj -c Release "-p:KitCoreDir=$KitCoreDir" -p:UseSharedCompilation=false
   if($LASTEXITCODE -eq 0){ dotnet tests\KitRepro\bin\Release\net8.0\KitRepro.dll }
  }
 } else { Write-Host "SKIP kit repro: UI Kit Core sources not found at $KitCoreDir (use -KitCoreDir)"; Add-Content -LiteralPath $logFile -Value "SKIP kit repro (no $KitCoreDir)" -Encoding UTF8 }
 $hash=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
 Write-Host ("PASS all {0} steps. DLL {1}  SHA256 {2}" -f $script:steps,$dll,$hash)
 Add-Content -LiteralPath $logFile -Value ("PASS all $($script:steps) steps; SHA256 $hash") -Encoding UTF8
} finally { Pop-Location }
