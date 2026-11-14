param([string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket', [string]$KeybindsDll='', [switch]$AllowNewGameBaseline)
$ErrorActionPreference='Stop'
if(!$KeybindsDll) { $KeybindsDll=Join-Path $GameDir 'BepInEx/plugins/SprocketKeybinds/SprocketKeybinds.dll' }
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
$checks=@(
 @('Sprocket.GameControl','Sprocket.GameControl.GameController','SceneUpdate',''),
 @('Sprocket.VehiclePartImporting','Sprocket.PartImporting.PartDefinitionCardFactory','CreateCard','Sprocket.PartImporting.PartDefinition'),
 @('Sprocket.Core','Sprocket.GameIcons','GetOrDefault','System.String'),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','Build',''),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','RequestModelLoad','System.String,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray'),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','GetModelScale',''),
 @('UnityEngine.AudioModule','UnityEngine.AudioClip','CreateUserSound_Injected','System.IntPtr,UnityEngine.Bindings.ManagedSpanWrapper&,System.Int32,System.Int32,System.Int32,System.Boolean'),
 @('UnityEngine.AudioModule','UnityEngine.AudioClip','SetData_Injected','System.IntPtr,UnityEngine.Bindings.ManagedSpanWrapper&,System.Int32'),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','OnModelLoaded','Il2CppSystem.Object,Sprocket.AssetManagement.AssetLoadCompleteEventArgs`1<Sprocket.Vehicles.IVehicleModel>'),
 @('Sprocket.VehicleController','Sprocket.Gameplay.VehicleControl.VehicleController','UpdateControl','Sprocket.GameTime,UnityEngine.Ray,Sprocket.Gameplay.VehicleControl.IVehicleControlInputs,UnityEngine.Camera'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleComponent','SaveData','Il2CppSystem.Runtime.Serialization.SerializationInfo,Sprocket.StateContext'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleComponent','LoadData','Il2CppSystem.Runtime.Serialization.SerializationInfo,Sprocket.StateContext'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleObject','Release',''),
 @('Unity.RenderPipelines.HighDefinition.Runtime','UnityEngine.Rendering.HighDefinition.HDMaterial','ValidateMaterial','UnityEngine.Material'),
 @('Unity.RenderPipelines.HighDefinition.Runtime','UnityEngine.Rendering.HighDefinition.HDMaterial','SetSurfaceType','UnityEngine.Material,System.Boolean')
)
foreach($c in $checks) {
 $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir "BepInEx/interop/$($c[0]).dll"))
 try {
  $t=$a.MainModule.Types | Where-Object FullName -EQ $c[1]
  $matches=@($t.Methods | Where-Object { $_.Name -ceq $c[2] -and (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',') -ceq $c[3] })
  if($matches.Count -ne 1) { throw "Missing or ambiguous signature $($c[1])::$($c[2])" }
  "PASS native contract $($c[1])::$($c[2])"
 } finally { $a.Dispose() }
}
& (Join-Path $PSScriptRoot "AuditInterop.ps1") -GameDir $GameDir
$baselineHash=(Get-FileHash (Join-Path $GameDir 'GameAssembly.dll')).Hash
if($baselineHash -ne '18A9A15B5E5F11898ED4DC34FC3E2D4C12950C3B37AC1FA499E8B00592DEDD56') {
 if(!$AllowNewGameBaseline) { throw 'Native game baseline changed; re-research required (or pass -AllowNewGameBaseline to continue with the signature checks only).' }
 "WARN native game baseline differs from the research baseline (GameAssembly SHA256 $baselineHash); continuing because -AllowNewGameBaseline was given. The signature/interop checks above and below still apply."
}
$api=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($KeybindsDll)
try {
 if($api.Name.Version -lt [Version]'0.1.5.0' -or $api.Name.Version -ge [Version]'0.2.4.0') { throw 'Unsupported build reference API version' }
 $type=$api.MainModule.Types | Where-Object FullName -EQ 'SprocketKeybinds.Keybinds'
 $button=@($type.Methods | Where-Object Name -EQ 'RegisterButton')
 if($button.Count -ne 1 -or $button[0].Parameters.Count -ne 6 -or !$button[0].Parameters[5].IsOptional) { throw 'Button contract changed' }
 if(!($type.Methods | Where-Object Name -EQ 'RegisterModMetadata')) { throw 'Missing current optional metadata API' }
 "PASS current API $($api.Name.Version), existing button contract and metadata export."
} finally { $api.Dispose() }
$dll=Join-Path $PSScriptRoot 'bin/Release/net6.0/SprocketSmokeLaunchers.dll'
$built=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
 if($built.Name.Version -ne [Version]'0.2.5.0') { throw 'Unexpected consumer version' }
 if($built.MainModule.AssemblyReferences | Where-Object Name -EQ 'UnityEngine.InputSystem') { throw 'Consumer must not reference direct keyboard polling' }
 $plugin=$built.MainModule.Types | Where-Object FullName -EQ 'SprocketSmokeLaunchers.Plugin'
 $dep=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' })
 if($dep.Count -ne 1 -or $dep[0].ConstructorArguments[0].Value -ne 'sprocket.keybinds' -or $dep[0].ConstructorArguments[1].Value -ne '>=0.1.5 <0.2.4') { throw 'Missing precise shared API dependency' }
 foreach($type in $built.MainModule.Types) { foreach($method in $type.Methods) {
  foreach($attributeTask in $method.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }) {
   if($attributeTask.ConstructorArguments.Count -ge 2) {
    $targetTypeTask=$attributeTask.ConstructorArguments[0].Value.FullName
    $targetNameTask=$attributeTask.ConstructorArguments[1].Value
    if($targetNameTask -in @('Enter','Exit','EnterInternal','ExitGame','SetupGame','ResetGame')) { throw 'Removed async lifecycle patch remains' }
   }
  }
  if(@($method.CustomAttributes | Where-Object { $_.AttributeType.FullName -like 'HarmonyLib.Harmony*' }).Count -and @($method.Parameters | Where-Object { $_.ParameterType.IsByReference }).Count) { throw "Unsafe by-ref Harmony callback $($method.FullName)" }
 }}
 if($built.MainModule.Types.Methods.Body.Instructions.Operand.FullName -match 'UnityEngine.Graphics::Blit|UnityEngine.Texture2D::ReadPixels|UnityEngine.RenderTexture::GetTemporary') { throw 'Removed GPU icon path remains' }
 'PASS built identity/dependency, no async lifecycle patches, no GPU icon path, no direct InputSystem/by-ref callbacks.' 
} finally { $built.Dispose() }
$part=Get-Content (Join-Path $PSScriptRoot 'assets/smokeLauncherBankPart.json') -Raw | ConvertFrom-Json
if($part.v -ne '0.3' -or $part.guid -ne 'ab3b7c19-7533-4dad-897f-ddb739234a16' -or $part.name -ne 'smokeLauncherBank' -or $part.date -ne '1945.01.01') { throw 'Incorrect identity/schema/era' }
if($part.components.Count -ne 1 -or $part.components[0].fileID -ne 'smokeLauncherBankModel' -or $part.components[0].type -ne 'model' -or $part.transform.objects.Count -ne 0 -or $part.components[0].info.damageable) { throw 'Invalid single-bank attachment schema' }
$nativePart=Get-Content (Join-Path $GameDir 'Sprocket_Data/StreamingAssets/Parts/germanSmokeLauncherPart.json') -Raw | ConvertFrom-Json
if($part.components[0].info.meshGuid -ne $nativePart.components[0].info.meshGuid -or $part.components[0].info.materialGuids[0] -ne $nativePart.components[0].info.materialGuids[0] -or 'crewEquipment' -notin $part.tags) { throw 'Native tri resource/category mismatch' }
foreach($propertyTask in $nativePart.transform.PSObject.Properties) {
 $ownValueTask=$part.transform.($propertyTask.Name) | ConvertTo-Json -Depth 10 -Compress
 $nativeValueTask=$propertyTask.Value | ConvertTo-Json -Depth 10 -Compress
 if($ownValueTask -ne $nativeValueTask) { throw "Native transform differs: $($propertyTask.Name)" }
}
foreach($fieldTask in @('scaling','flipping','selectable','placementSurface','damageable')) {
 if($part.components[0].info.($fieldTask) -ne $nativePart.components[0].info.($fieldTask)) { throw "Native model field differs: $fieldTask" }
}
$pluginSourceTask=Get-Content (Join-Path $PSScriptRoot 'Plugin.cs') -Raw
if($pluginSourceTask -match 'model.MarkDynamic\(') { throw 'Own forced dynamic override remains' }
if($pluginSourceTask -notmatch 'LaunchVolume",.6f' -or $pluginSourceTask -match 'SoundVolume.Value\s*=') { throw 'Existing volume setting migration introduced' }
'PASS all native transform properties/model placement flags match standard tri; no dynamic override or volume config migration.'
'PASS native tri mesh/material reuse and Crew Equipment category.' 
$collisions=Get-ChildItem (Join-Path $GameDir 'Sprocket_Data/StreamingAssets/Parts') -Filter '*.json' | Where-Object Name -NE 'smokeLauncherBankPart.json' | Select-String -SimpleMatch $part.guid
if($collisions) { throw 'Native part GUID collision' }
$xml=[xml](Get-Content (Join-Path $PSScriptRoot 'assets/smokeLauncherBank.xml') -Raw)
if($xml.Part.name -ne 'Smoke Launcher Bank' -or $xml.Part.description -notmatch 'Settings') { throw 'Localization/control help missing' }
'PASS part identity, native schema, native placement/mirroring, localization and no installed GUID collision.'
'CHECKS COMPLETE. Native gameplay and rendering validation are still pending.'


