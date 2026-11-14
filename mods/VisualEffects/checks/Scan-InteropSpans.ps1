[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModDll,
    [string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
    [string]$Report
)
# Static interop scan (read-only on the game): every Unity/Il2Cpp call in the
# mod DLL is followed into the generated wrapper IL. FAIL families: span (missing
# ReadOnlySpan/Span.GetPinnableReference), missing member, stripped (no native code /
# failed unstripping), alias (NativeMethodInfoPtr field holds another method, e.g.
# the generic definition) and icall (name not in UnityPlayer.dll). Native checks use
# MelonLoader's Cpp2IL dump, verified against the GameAssembly SHA512.
# Probes prove each family: 0.2.0 (span) and 0.2.1 (alias) in-game failures and a
# failed-unstrip method must FAIL; the 0.2.2 route and 0.1.0 calls that worked must PASS.
$ErrorActionPreference='Stop'
if(-not $Report){$Report=Join-Path ([IO.Path]::GetTempPath()) ('sprocketvfx-interop-scan-'+[Guid]::NewGuid().ToString('N')+'.txt')}
$tool=Join-Path $PSScriptRoot 'InteropScan\InteropScan.csproj'
dotnet build $tool -c Release --nologo -v q "-p:GameDir=$GamePath" | Out-Null
if($LASTEXITCODE){throw 'InteropScan build failed.'}
$exe=Join-Path $PSScriptRoot 'InteropScan\bin\Release\net8.0\InteropScan.dll'
dotnet $exe $ModDll $GamePath $Report `
    --expect-fail 'UnityEngine.AssetBundle::LoadFromFile(System.String)' `
    --expect-fail 'UnityEngine.AssetBundle::LoadAllAssets()' `
    --expect-fail 'UnityEngine.AssetBundle::LoadFromMemory(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>)' `
    --expect-fail 'UnityEngine.AssetBundle::LoadAllAssetsAsync()' `
    --expect-fail 'UnityEngine.MaterialPropertyBlock::ExtractFloatArrayImpl(System.Int32,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Single>)' `
    --expect-pass 'UnityEngine.AssetBundle::LoadFromFileAsync(System.String)' `
    --expect-pass 'UnityEngine.AssetBundle::LoadAllAssetsAsync(Il2CppSystem.Type)' `
    --expect-pass 'UnityEngine.AssetBundle::LoadAssetAsync(System.String,Il2CppSystem.Type)' `
    --expect-pass 'UnityEngine.ImageConversion::LoadImage(UnityEngine.Texture2D,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.Boolean)' `
    --expect-pass 'UnityEngine.Shader::Find(System.String)'
if($LASTEXITCODE){throw "Interop scan FAILED (see $Report)."}
