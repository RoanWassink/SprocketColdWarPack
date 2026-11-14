# Build and test Sprocket Oscillating Turret, one transcript. Nothing is installed and the game folder is only read.
#   powershell -NoProfile -File .\check.ps1 -GameDir <folder with BepInEx\core and BepInEx\interop> -WorkDir <scratch folder> `
#       [-GameRoot <game folder to read>] [-NativeMethodsJson <all-native-methods.json>]
# Steps: 1 mesh data regenerated from the STLs must equal src\HeavyTrunnionMeshData.cs; 2 build (0 errors) and a second,
# non-incremental build that must give the same SHA256 (deterministic, no pdb path in the DLL); 3 part JSONs and localization
# next to the DLL equal their sources; 4 the test project (needs the built DLL); 5 the ICF audit of the native hook targets
# (only with -NativeMethodsJson; needs the game's MLLoader Cpp2IL output, read only); 6 SHA256 summary.
# Exit 0 only when everything passes. Log: <WorkDir>\check.log.
param(
  [Parameter(Mandatory = $true)][string]$GameDir,
  [Parameter(Mandatory = $true)][string]$WorkDir,
  [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
  [string]$NativeMethodsJson = '',
  [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\SprocketOscillatingTurret.csproj'
$tests = Join-Path $root 'tests\OT.Tests.csproj'
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$out = Join-Path $WorkDir 'out'
$out2 = Join-Path $WorkDir 'out-second'
$log = Join-Path $WorkDir 'check.log'
$lines = [System.Collections.Generic.List[string]]::new()
function Say([string]$text) { $lines.Add($text); Write-Output $text }
function Finish([int]$code) { $lines | Out-File -LiteralPath $log -Encoding utf8; exit $code }
trap { Say "CHECK FAILED: $_"; Finish 1 }

Say "check.ps1 $(Get-Date -Format o)"
$env:SPROCKET_DIR = $GameRoot   # the tests read the game's Parts folder from here (read only)

# 1. embedded mesh data == conversion of the current STLs
$regen = Join-Path $WorkDir 'HeavyTrunnionMeshData.regen.cs'
$conv = & py -I (Join-Path $root 'tests\tools\heavy_mesh_to_cs.py') (Join-Path $root 'model') $regen 2>&1
$conv | ForEach-Object { Say "mesh: $_" }
if ($LASTEXITCODE -ne 0) { Say 'MESH CONVERSION FAILED'; Finish 1 }
$a = (Get-FileHash -LiteralPath $regen -Algorithm SHA256).Hash
$b = (Get-FileHash -LiteralPath (Join-Path $root 'src\HeavyTrunnionMeshData.cs') -Algorithm SHA256).Hash
Say "mesh data: regenerated $a vs src $b -> $(if ($a -eq $b) { 'SAME' } else { 'DIFFERENT' })"
if ($a -ne $b) { Say 'MESH DATA OUT OF DATE'; Finish 1 }

# 2. build, then a second non-incremental build into another folder
foreach ($target in @(@($out, @()), @($out2, @('--no-incremental')))) {
  $build = & dotnet build $project -c $Configuration -o $target[0] "-p:GameDir=$GameDir" @($target[1]) 2>&1
  $buildExit = $LASTEXITCODE
  $build | ForEach-Object { Say "build: $_" }
  $errors = ($build | Select-String -Pattern '^\s*(\d+) Error\(s\)' | Select-Object -Last 1).Matches.Groups[1].Value
  if ($buildExit -ne 0 -or $errors -ne '0') { Say "BUILD FAILED exit=$buildExit errors=$errors"; Finish 1 }
}
$dll = Join-Path $out 'SprocketOscillatingTurret.dll'
$h1 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$h2 = (Get-FileHash -LiteralPath (Join-Path $out2 'SprocketOscillatingTurret.dll') -Algorithm SHA256).Hash
Say "deterministic: first $h1, second $h2 -> $(if ($h1 -eq $h2) { 'SAME' } else { 'DIFFERENT' })"
if ($h1 -ne $h2) { Say 'BUILD NOT DETERMINISTIC'; Finish 1 }
if (Get-ChildItem -LiteralPath $out -Filter '*.pdb') { Say 'PDB WAS BUILT'; Finish 1 }

# 3. deliverables next to the DLL equal their sources
$files = @(
  @('heavyTrunnionMantletPart.json', 'src\parts\heavyTrunnionMantletPart.json'),
  @('heavyTrunnionDrivePart.json', 'src\parts\heavyTrunnionDrivePart.json'),
  @('heavyTrunnionMantlet.xml', 'src\localization\heavyTrunnionMantlet.xml'))
foreach ($f in $files) {
  $built = Join-Path $out $f[0]
  if (-not (Test-Path -LiteralPath $built)) { Say "MISSING $built"; Finish 1 }
  $src = (Get-FileHash -LiteralPath (Join-Path $root $f[1]) -Algorithm SHA256).Hash
  $dst = (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash
  if ($src -ne $dst) { Say "OUT DIFFERS FROM SOURCE: $($f[0])"; Finish 1 }
}

# 4. tests (explicit managed host avoids Windows apphost initialization failures)
$testBuild = & dotnet build $tests -c $Configuration -p:UseAppHost=false 2>&1
$testBuildExit = $LASTEXITCODE
$testBuild | ForEach-Object { Say "test-build: $_" }
if ($testBuildExit -ne 0) { Say "TEST BUILD FAILED exit=$testBuildExit"; Finish 1 }
$testAssembly = Join-Path $root "tests\bin\$Configuration\net8.0\OT.Tests.dll"
$testOutput = & dotnet $testAssembly $dll 2>&1
$testExit = $LASTEXITCODE
$testOutput | ForEach-Object { Say "test: $_" }
if ($testExit -ne 0) { Say "TESTS FAILED exit=$testExit"; Finish 1 }

# 5. source-derived native ICF audit (read-only metadata extraction)
if ($NativeMethodsJson) {
  $meta = Join-Path $WorkDir 'icf-metadata.json'
  $report = Join-Path $WorkDir 'icf-targets.json'
  $export = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\Export-IcfMetadata.ps1') -GameDir $GameRoot -Output $meta 2>&1
  $exportExit = $LASTEXITCODE
  $export | ForEach-Object { Say "metadata: $_" }
  if ($exportExit -ne 0) { Say "METADATA EXPORT FAILED exit=$exportExit"; Finish 1 }
  $icf = & py -I (Join-Path $root 'tests\icf_audit.py') $NativeMethodsJson $meta $report 2>&1
  $icfExit = $LASTEXITCODE
  $icf | ForEach-Object { Say "icf: $_" }
  if ($icfExit -ne 0) { Say "ICF TEST FAILED exit=$icfExit"; Finish 1 }
} else {
  Say 'ICF audit skipped (no -NativeMethodsJson).'
}

# 6. summary
Say "BUILD OK 0 errors; deterministic; TESTS OK$(if ($NativeMethodsJson) { '; ICF OK' } else { '; ICF skipped' })"
Say ("SHA256 {0}  {1}" -f $h1, $dll)
Finish 0
