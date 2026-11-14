<#
.SYNOPSIS
    Single offline check round for Sprocket Reference Images: build, offline tests, H3 ICF audit, interop scan.
    Reads the game directory, never writes to it, installs nothing, does not start the game.
.PARAMETER RefDir
    Directory with BepInEx\core, BepInEx\interop, unity-libs and the plugins SprocketUiKit and SprocketKeybinds
    (build references; the game directory itself or a copy of it).
.PARAMETER GameRoot
    The real game directory, read-only: native part JSONs for the schema tests, GameAssembly.dll for the H3 audit.
.PARAMETER OutDir
    Scratch directory for the build output, the package files and the reports (outside the source tree).
.PARAMETER NativeMethodsJson
    all-native-methods.json of the ICF audit (read-only). Without it the H3 audit step is skipped.
.PARAMETER InteropScanner
    Path to InteropScan.dll (read-only tool). Without it the interop-scan step is skipped.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$RefDir,
  [string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
  [Parameter(Mandatory=$true)][string]$OutDir,
  [string]$NativeMethodsJson='',
  [string]$InteropScanner=''
)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$build=Join-Path $OutDir 'build'
Write-Output 'Sprocket Reference Images - single check round. No installation or game launch.'
& dotnet build (Join-Path $project 'SprocketReferenceImages.csproj') -c Release --nologo -v minimal "-p:GameDir=$RefDir" -o $build 2>&1 | ForEach-Object {Write-Host $_}
if($LASTEXITCODE -ne 0){throw 'Plugin build failed.'}
$dll=Join-Path $build 'SprocketReferenceImages.dll'
& dotnet run --project (Join-Path $project 'tests\SprocketReferenceImages.Tests.csproj') -c Release -- $project $GameRoot $dll 2>&1 | ForEach-Object {Write-Host $_}
if($LASTEXITCODE -ne 0){throw 'Offline tests failed.'}
if($NativeMethodsJson){
  & (Join-Path $PSScriptRoot 'Check-Native.ps1') -GameDir $GameRoot -Report (Join-Path $OutDir 'native.txt') -NativeMethodsJson $NativeMethodsJson
} else { Write-Warning 'H3 ICF audit skipped (no -NativeMethodsJson).' }
if($InteropScanner){
  & dotnet $InteropScanner $dll $GameRoot (Join-Path $OutDir 'interop.txt') `
    --expect-fail 'UnityEngine.AssetBundle::LoadFromFile(System.String)' `
    --expect-fail 'UnityEngine.AssetBundle::LoadAllAssetsAsync()' `
    --expect-fail 'UnityEngine.MaterialPropertyBlock::ExtractFloatArrayImpl(System.Int32,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Single>)' `
    --expect-pass 'UnityEngine.ImageConversion::LoadImage(UnityEngine.Texture2D,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.Boolean)' `
    --expect-pass 'UnityEngine.ImageConversion::EncodeToPNG(UnityEngine.Texture2D)' 2>&1 | ForEach-Object {Write-Host $_}
  if($LASTEXITCODE -ne 0){throw 'Actual DLL interop scan failed.'}
} else { Write-Warning 'Interop scan skipped (no -InteropScanner).' }
Write-Output ('SHA256 '+(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash+'  SprocketReferenceImages.dll')
Write-Output 'RESULT: BUILD + TESTS + H3 ICF + ACTUAL DLL SCAN PASS. No game test performed.'
