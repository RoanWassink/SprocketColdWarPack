[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModDll,
    # Real game folder, READ ONLY: GameAssembly.dll, UnityPlayer.dll, interop and the Cpp2IL dump.
    [string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
    # Folder with BepInEx\core to compile the scanner against (default: the game folder).
    [string]$BuildGameDir='',
    [Parameter(Mandatory)][string]$Report,
    # Optional: folder laid out like MLLoader\MelonLoader\Dependencies\Il2CppAssemblyGenerator (Config.cfg + Cpp2IL\cpp2il_out). Default: inside the game folder.
    [string]$GeneratorDir=''
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
if(-not $BuildGameDir){$BuildGameDir=$GamePath}
$tool=Join-Path $PSScriptRoot 'InteropScan\InteropScan.csproj'
dotnet build $tool -c Release --nologo -v q "-p:GameDir=$BuildGameDir" | Out-Null
if($LASTEXITCODE){throw 'InteropScan build failed.'}
$exe=Join-Path $PSScriptRoot 'InteropScan\bin\Release\net8.0\InteropScan.dll'
$gen=@(); if($GeneratorDir){$gen=@('--generator-dir',$GeneratorDir)}
dotnet $exe $ModDll $GamePath $Report @gen `
    --expect-fail 'UnityEngine.AssetBundle::LoadFromFile(System.String)' `
    --expect-fail 'UnityEngine.AssetBundle::LoadAllAssets()' `
    --expect-fail 'UnityEngine.AssetBundle::LoadFromMemory(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>)' `
    --expect-fail 'UnityEngine.AssetBundle::LoadAllAssetsAsync()' `
    --expect-fail 'UnityEngine.MaterialPropertyBlock::ExtractFloatArrayImpl(System.Int32,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Single>)' `
    --expect-pass 'UnityEngine.AssetBundle::LoadFromFileAsync(System.String)' `
    --expect-pass 'UnityEngine.AssetBundle::LoadAllAssetsAsync(Il2CppSystem.Type)' `
    --expect-pass 'UnityEngine.AssetBundle::LoadAssetAsync(System.String,Il2CppSystem.Type)' `
    --expect-pass 'UnityEngine.ImageConversion::LoadImage(UnityEngine.Texture2D,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.Boolean)' `
    --expect-pass 'UnityEngine.Shader::Find(System.String)' `
    --expect-pass 'Sprocket.Vehicles.Cannons.Cannon::get_Blueprint()' `
    --expect-pass 'Sprocket.Vehicles.Cannons.CannonBlueprint::get_Caliber()' `
    --expect-pass 'Sprocket.Vehicles.Cannons.CannonBlueprint::get_PropellantLength()' `
    --expect-pass 'Sprocket.Vehicles.Cannons.Cannon::get_ShellBlueprint()' `
    --expect-pass 'Sprocket.Vehicles.AmmunitionStorage.ShellSlotBlueprint::get_Diameter()' `
    --expect-pass 'Sprocket.Vehicles.AmmunitionStorage.ShellSlotBlueprint::get_PropellantLength()' `
    --expect-pass 'Sprocket.Vehicles.VehicleComponent::get_IsInstalled()' `
    --expect-pass 'Sprocket.Vehicles.AmmunitionStorage.AmmoRackModel::get_propellantLength()' `
    --expect-pass 'Sprocket.Vehicles.AmmunitionStorage.AmmoRackModel::get_projectileLength()' `
    --expect-pass 'Sprocket.Vehicles.AmmunitionStorage.AmmoRackModel::get_propellantModelLength()' `
    --expect-pass 'Sprocket.Vehicles.AmmunitionStorage.AmmoRackModel::get_projectileModelLength()' `
    --expect-pass 'Sprocket.Vehicles.AmmunitionStorage.AmmoRackModel::get_loadedMaterial()' `
    --expect-pass 'UnityEngine.Material::.ctor(UnityEngine.Material)' `
    --expect-pass 'UnityEngine.Material::HasProperty(System.String)' `
    --expect-pass 'UnityEngine.Material::GetTexture(System.String)' `
    --expect-pass 'UnityEngine.Material::get_mainTexture()' `
    --expect-pass 'UnityEngine.Material::get_shader()' `
    --expect-pass 'UnityEngine.Material::get_shaderKeywords()' `
    --expect-pass 'UnityEngine.Mesh::get_vertexCount()' `
    --expect-pass 'UnityEngine.Object::get_name()' `
    --expect-pass 'UnityEngine.Renderer::set_sharedMaterial(UnityEngine.Material)' `
    --expect-pass 'UnityEngine.Renderer::set_receiveShadows(System.Boolean)' `
    --expect-pass 'UnityEngine.Renderer::set_lightProbeUsage(UnityEngine.Rendering.LightProbeUsage)' `
    --expect-pass 'UnityEngine.Renderer::set_reflectionProbeUsage(UnityEngine.Rendering.ReflectionProbeUsage)'
if($LASTEXITCODE){throw "Interop scan FAILED (see $Report)."}
