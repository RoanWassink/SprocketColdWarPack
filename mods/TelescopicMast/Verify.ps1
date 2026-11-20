# Checks the source tree and the built DLL. Writes nothing.
#  -GameDir   the Sprocket game folder; READ ONLY: interop, Mono.Cecil, GameAssembly.dll hash, installed part definitions.
#  -GameRef   folder with the Keybinds and UI Kit DLLs the mast is built against (BepInEx\plugins\...); default: GameDir.
#  -DllPath   the built mast DLL; default bin\Release\net6.0\SprocketTelescopicMast.dll.
#  -IconChecker  house-style icon checker; default icon-src\Make-PartIcon.ps1.
param(
 [string]$GameDir='E:\SteamLibrary\steamapps\common\Sprocket',
 [string]$GameRef='',
 [string]$DllPath='',
 [string]$IconChecker=''
)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($GameRef)){ $GameRef=$GameDir }
if([string]::IsNullOrWhiteSpace($IconChecker)){ $IconChecker=Join-Path $PSScriptRoot 'icon-src\Make-PartIcon.ps1' }
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
# Patch targets: assembly, type, method, exact parameter types (interop of the installed game).
$checks=@(
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleObject','EnablePhysics','Sprocket.Vehicles.IBehaviourEnableContext'),
 @('Sprocket.VehiclePartImporting','Sprocket.PartImporting.PartDefinitionCardFactory','CreateCard','Sprocket.PartImporting.PartDefinition'),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','Build',''),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','OnModelLoaded','Il2CppSystem.Object,Sprocket.AssetManagement.AssetLoadCompleteEventArgs`1<Sprocket.Vehicles.IVehicleModel>'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleObject','DisablePhysics',''),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleObject','Release',''),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleTransform','SaveState','Il2CppSystem.Runtime.Serialization.SerializationInfo,Sprocket.StateContext'),
 # 0.4.0 hinge + I/O (0.7.1: GameUpdate, UpdateControl and Launch are no longer hooked)
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleObjectSerialization','ToBlueprint','Sprocket.Vehicles.VehicleObject,Sprocket.StateContext'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleComponent','LoadData','Il2CppSystem.Runtime.Serialization.SerializationInfo,Sprocket.StateContext'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleTransform','LoadState','Il2CppSystem.Runtime.Serialization.SerializationInfo,Sprocket.StateContext'),
 @('Sprocket.WeaponFramework','Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour','FireInternal','Sprocket.DamageModelling.ProjectileTypeID'),
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.Cannon','Build',''),
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.Cannon','OnVehicleBuilt','Sprocket.Vehicles.VehicleDirtyFlags'),
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.Cannon','Release','')
)
# Members read or written (not hooked): assembly, type, method, return type.
$members=@(
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.CannonInstanceBlueprint','get_FireGroup','System.UInt16'),
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.Cannon','get_InstanceBlueprint','Sprocket.Vehicles.Cannons.CannonInstanceBlueprint'),
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.Cannon','get_Behaviour','Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour'),
 @('Sprocket.WeaponFramework','Sprocket.WeaponFramework.WeaponBehaviour','get_LoadState','Sprocket.WeaponFramework.LoadState'),
 @('Sprocket.WeaponFramework','Sprocket.WeaponFramework.WeaponBehaviour','get_FireGroup','System.Int32'),
 @('Sprocket.WeaponFramework','Sprocket.WeaponFramework.WeaponBehaviour','get_mount','Sprocket.WeaponFramework.IWeaponMount'),
 @('Sprocket.Projectiles','Sprocket.DamageModelling.ProjectileRegister','get_activeIdMap','Il2CppSystem.Collections.Generic.Dictionary`2<System.Int32,System.Int32>'),
 @('Sprocket.Projectiles','Sprocket.DamageModelling.ProjectileRegister','get_nextID','System.Int32'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleTransform','set_Rotation','System.Void'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleTransform','NotifyTransformChanged','System.Void'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleBehaviour','get_ControlType','Sprocket.Vehicles.Control.ControlType'),
 @('Il2Cppmscorlib','Il2CppSystem.Runtime.Serialization.SerializationInfo','GetString','System.String'),
 # 0.5.0: cannon choice labels, hinge/cannon VUIDs, hidden mount
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.Cannon','get_Blueprint','Sprocket.Vehicles.Cannons.CannonBlueprint'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehiclePart','get_VUID','Sprocket.Vehicles.VUID'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleObject','get_Vehicle','Sprocket.Vehicles.IVehicleGateway'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.VehicleComponent','get_Vehicle','Sprocket.Vehicles.IVehicleGateway'),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','get_Model','Sprocket.Vehicles.IVehicleModel'),
 @('Sprocket.VehicleFramework','Sprocket.Vehicles.IVehicleModel','SetVisible','System.Void')
)
# Fields read (Il2CppInterop exposes them as properties with get_ accessors over the native field).
$fields=@(
 @('Sprocket.Vehicles.Cannons','Sprocket.Vehicles.Cannons.CannonBlueprint','Caliber','System.UInt16'),
 @('Sprocket.Vehicles','Sprocket.Vehicles.VehicleObjectModel','visible','System.Boolean')
)
$assemblies=@{}
function Asm($name){ if(!$assemblies.ContainsKey($name)){ $assemblies[$name]=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir "BepInEx/interop/$name.dll")) }; $assemblies[$name] }
try {
 foreach($c in $checks) {
  $t=(Asm $c[0]).MainModule.Types | Where-Object FullName -EQ $c[1]
  if(!$t) { throw "Missing type $($c[1])" }
  $m=@($t.Methods | Where-Object Name -EQ $c[2])
  if($m.Count -ne 1) { throw "Missing or ambiguous $($c[1])::$($c[2])" }
  $signature=($m[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ','
  if($signature -cne $c[3]) { throw "Changed signature $($c[1])::$($c[2]): $signature" }
  "PASS $($c[1])::$($c[2])"
 }
 foreach($c in $members) {
  $t=(Asm $c[0]).MainModule.Types | Where-Object FullName -EQ $c[1]
  $m=@($t.Methods | Where-Object { $_.Name -eq $c[2] -and $_.ReturnType.FullName -eq $c[3] })
  if($m.Count -lt 1) { throw "Missing member $($c[1])::$($c[2]) returning $($c[3])" }
 }
 "PASS $($members.Count) read/write members used by the hinge I/O."
 foreach($c in $fields) {
  $t=(Asm $c[0]).MainModule.Types | Where-Object FullName -EQ $c[1]
  $prop=@($t.Properties | Where-Object { $_.Name -eq $c[2] -and $_.PropertyType.FullName -eq $c[3] })
  if($prop.Count -ne 1) { throw "Missing field accessor $($c[1])::$($c[2]) ($($c[3]))" }
 }
 $collider=(Asm 'Sprocket.Vehicles').MainModule.Types | Where-Object FullName -EQ 'Sprocket.Vehicles.Colliders.ColliderType'
 if(!$collider) { foreach($n in @('Sprocket.VehicleFramework','Sprocket.Vehicles.Colliders')) { try { $collider=(Asm $n).MainModule.Types | Where-Object FullName -EQ 'Sprocket.Vehicles.Colliders.ColliderType'; if($collider){break} } catch {} } }
 $flags=@{}; foreach($f in @($collider.Fields | Where-Object { $_.HasConstant })) { $flags[$f.Name]=[int]$f.Constant }
 if($flags['Physics'] -ne 1 -or $flags['Detection'] -ne 0x20 -or $flags['Selection'] -ne 2 -or $flags['PlacementSurface'] -ne 0x10) { throw 'ColliderType flags changed (mount strips Physics|Detection, keeps Selection|PlacementSurface).' }
 "PASS $($fields.Count) field accessors and ColliderType flags used by the 0.5.0 mount and cannon choice."
} finally { foreach($a in $assemblies.Values){ $a.Dispose() } }
$hash=(Get-FileHash -LiteralPath (Join-Path $GameDir 'GameAssembly.dll')).Hash
# Steam build 26757044, reaudited 2026-11-18.
if($hash -ne '77FC601748991DC2101435C6B72C7A5A4B3DC3C68429B349C2EFEE711F6D5388') { throw "Game binary changed ($hash); recheck before installation." }
$part=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'assets/telescopicSightMastPart.json') -Raw | ConvertFrom-Json
if($part.guid -ne 'e93f8788-e748-4bdf-bf91-dd236dc3f8aa' -or $part.components.Count -ne 1 -or !$part.transform.dynamic -or !$part.transform.scaling) { throw 'Invalid mast schema.' }
if($part.transform.objects.Count -ne 1 -or $part.transform.objects[0].components[0].fileID -ne 'sprocketMastSleeve') { throw 'Sleeve needs its own component transform.' }
foreach($axis in 0..2) { if([double]$part.transform.minScale[$axis] -ne 0.1 -or [double]$part.transform.maxScale[$axis] -ne 5) { throw 'Incorrect scale range.' } }
foreach($component in @($part.components) + @($part.transform.objects[0].components)) { if($component.type -ne 'model' -or !$component.info.placementSurface) { throw 'Mast must provide model mounting surfaces.' } }
$existing=Get-ChildItem (Join-Path $GameDir 'Sprocket_Data/StreamingAssets/Parts') -Filter '*.json' | Where-Object Name -NE 'telescopicSightMastPart.json' | Select-String -SimpleMatch $part.guid
if($existing) { throw 'Part GUID collides with another definition.' }
foreach($kind in @('Base','Part')) {
 $file='telescope'+$kind+'Part.json'
 $split=Get-Content -LiteralPath (Join-Path $PSScriptRoot ('assets/'+$file)) -Raw | ConvertFrom-Json
 if($split.components.Count -ne 1 -or $split.transform.objects.Count -ne 0 -or !$split.transform.scaling -or !$split.transform.dynamic) { throw "Invalid separate $kind topology" }
 if($split.components[0].fileID -ne ('sprocketTelescope'+$kind+'Model') -or $split.tags -notcontains ('sprocketTelescope'+$kind)) { throw "Invalid $kind identity" }
 $collision=Get-ChildItem (Join-Path $GameDir 'Sprocket_Data/StreamingAssets/Parts') -Filter '*.json' | Where-Object Name -NE $file | Select-String -SimpleMatch $split.guid
 if($collision) { throw "GUID collision for $kind" }
 "PASS independent Telescope $kind identity and single-model topology."
}
# Hinge parts: identity, topology, earliest-date availability, names unique among game parts.
$hinge=@(
 @('rotatorBasePart.json','a0955a06-58b8-4da3-9dc5-5080cb54c96b','rotatorBase','sprocketRotatorBase','sprocketRotatorBaseModel','rotatorBase.xml','rotator-base-icon.png'),
 @('rotatorPlatePart.json','08add1ba-efce-4246-a7a6-30b69297e7e6','rotatorPlate','sprocketRotatorPlate','sprocketRotatorPlateModel','rotatorPlate.xml','rotator-plate-icon.png'),
 @('hingeBasePart.json','754d193e-e56a-4740-9138-0852fb0ef119','hingeBase','sprocketHingeBase','sprocketHingeBaseModel','hingeBase.xml','hinge-base-icon.png'),
 @('hingeLeafPart.json','b907b6b7-ef88-4d12-aeb5-75e80ab53875','hingeLeaf','sprocketHingeLeaf','sprocketHingeLeafModel','hingeLeaf.xml','hinge-leaf-icon.png'),
 @('hingeMountPart.json','5c1e9a37-2d4b-4f86-9e0a-8b7d3f61c2a4','hingeMount','sprocketHingeMount','sprocketHingeMountModel','hingeMount.xml','hinge-mount-icon.png')
)
foreach($h in $hinge) {
 $j=Get-Content -LiteralPath (Join-Path $PSScriptRoot ('assets/'+$h[0])) -Raw | ConvertFrom-Json
 if($j.guid -ne $h[1] -or $j.name -ne $h[2] -or $j.tags -notcontains $h[3] -or $j.components.Count -ne 1 -or $j.components[0].fileID -ne $h[4]) { throw "Invalid hinge identity $($h[0])" }
 if(!$j.transform.dynamic -or !$j.transform.scaling -or $j.transform.objects.Count -ne 0 -or !$j.components[0].info.placementSurface) { throw "Invalid hinge topology $($h[0])" }
 if($j.date -ne '0000.01.01') { throw "Hinge must be available from the earliest date: $($h[0])" }
 foreach($axis in 0..2) { if([double]$j.transform.minScale[$axis] -ne 0.1 -or [double]$j.transform.maxScale[$axis] -ne 5) { throw "Incorrect hinge scale range $($h[0])" } }
 $others=Get-ChildItem (Join-Path $GameDir 'Sprocket_Data/StreamingAssets/Parts') -Filter '*.json' | Where-Object Name -NE $h[0]
 if($others | Select-String -SimpleMatch $h[1]) { throw "GUID collision for $($h[0])" }
 if($others | Select-String -Pattern ('"name":\s*"'+$h[2]+'"')) { throw "Part name collision for $($h[2])" }
 [xml]$x=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot ('assets/'+$h[5]))
 if(!$x.Part.name -or $x.Part.description -notmatch 'Settings keybind menu') { throw "Hinge localization $($h[5]) incomplete" }
 if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot ('assets/'+$h[6])))) { throw "Missing icon $($h[6])" }
 "PASS hinge part $($h[2]) identity, topology, date, localization and icon."
}
$mount=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'assets/hingeMountPart.json') -Raw | ConvertFrom-Json
if($mount.components[0].info.damageable -ne $false -or !$mount.components[0].info.selectable) { throw 'Hinge Mount must be non-damageable (no hidden armour) and selectable in the designer.' }
$icons=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'IconHooks.cs')
foreach($h in $hinge) { if($icons -notmatch [regex]::Escape('"'+$h[1]+'" => "'+$h[6]+'"')) { throw "Icon hook missing for $($h[2])" } }
'PASS Hinge Mount non-damageable/selectable and icon hooks.'
$source=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Plugin.cs')
if($source -notmatch 'Config\.Bind\("Diagnostics", "DetailedLogging", false,') { throw 'Diagnostics.DetailedLogging must exist (default false).' }
if($source -notmatch '"sprocket\.telescopicmast"' -or $source -notmatch 'BepInPlugin\(ModGuid, "Sprocket Telescopic Mast", "0.9.9"\)') { throw 'Mast GUID or version changed.' }
$dll=if([string]::IsNullOrWhiteSpace($DllPath)){Join-Path $PSScriptRoot 'bin/Release/net6.0/SprocketTelescopicMast.dll'}else{$DllPath}
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
 foreach($type in $a.MainModule.Types) { foreach($method in $type.Methods) {
  $harmony=@($method.CustomAttributes | Where-Object { $_.AttributeType.FullName -like 'HarmonyLib.Harmony*' }).Count
  # No Harmony callback may take a by-ref or value-type parameter (0.7.1b: the WeaponFireInfo
  # result exception is gone; the fire pair reads the class ProjectileRegister counter instead).
  if($harmony) { foreach($p in $method.Parameters) {
   if($p.ParameterType.IsByReference) { throw "By-ref parameter in Harmony callback $($method.FullName): $($p.Name)" }
   if($p.ParameterType.IsValueType) { throw "Value-type parameter in Harmony callback $($method.FullName): $($p.Name)" }
  } }
  if($type.Name -eq 'HingeFireHooks' -and ($method.Name -eq 'BeforeFire' -or $method.Name -eq 'AfterFire')) {
   if(@($method.Parameters).Count -ne 1 -or $method.Parameters[0].Name -ne '__instance' -or $method.Parameters[0].ParameterType.FullName -ne 'Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour') {
    throw "$($method.Name) must take only CannonBehaviour __instance."
   }
  }
 }}
 # 0.7.1: exact callback set, removed hooks absent, no value-type or by-ref callback parameter.
 $hookCheck = & (Join-Path $PSScriptRoot 'tools\Check-Hooks.ps1') -DllPath $dll -GameDir $GameDir
 $hookCheck
 # The replacement loops are real MonoBehaviour methods, not dead code.
 function CallsOf($type,$name){
  $m=$type.Methods | Where-Object Name -eq $name
  if(!$m -or !$m.HasBody){ return @() }
  return @($m.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
 }
 $hingeHost=$a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.HingeHost'
 if(!(CallsOf $hingeHost 'LateUpdate' | Where-Object { $_ -match 'HingeRuntime::TickAll' })){ throw 'HingeHost.LateUpdate must run HingeRuntime.TickAll.' }
 $mastDriver=$a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.MastDriver'
 if(!(CallsOf $mastDriver 'Update' | Where-Object { $_ -match 'MastRuntime::Update' })){ throw 'MastDriver.Update must run MastRuntime.Update.' }
 if(!(CallsOf $mastDriver 'LateUpdate' | Where-Object { $_ -match 'MastRuntime::RefreshAll' })){ throw 'MastDriver.LateUpdate must run MastRuntime.RefreshAll.' }
 # Save merges in place through the pure policy and SerializationInfo.UpdateValue.
 $save=($a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.HingeRuntime').Methods | Where-Object Name -eq 'Save'
 $saveCalls=@($save.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
 if(!($saveCalls | Where-Object { $_ -match 'Io.StateMerge::Merge' })){ throw 'Save must call the pure StateMerge policy.' }
 $bag=($a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.HingeRuntime').NestedTypes | Where-Object Name -eq 'SerializationStateBag'
 if(!$bag){ throw 'SerializationStateBag adapter is missing.' }
 $bagCalls=@(foreach($m in $bag.Methods){ if($m.HasBody){ foreach($i in $m.Body.Instructions){ if($i.Operand -is [Mono.Cecil.MethodReference]){ $i.Operand.FullName } } } })
 if(!($bagCalls | Where-Object { $_ -match 'SerializationInfo::UpdateValue' }) -or !($bagCalls | Where-Object { $_ -match 'SerializationInfo::AddValue' })){ throw 'State bag must update or add the key, never rebuild the state.' }
 $fire=$a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.HingeFire'
 foreach($name in @('BeginFire','EndFire')) {
  $m=@($fire.Methods | Where-Object Name -eq $name)
  if($m.Count -ne 1 -or @($m[0].Parameters).Count -ne 1 -or $m[0].Parameters[0].ParameterType.FullName -ne 'Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour'){ throw "HingeFire.$name must take only CannonBehaviour (no result struct)." }
 }
 'PASS 0.7.1 replacement paths: HingeHost.LateUpdate/TickAll, MastDriver.Update/Update, LateUpdate/RefreshAll, in-place StateMerge and the native ProjectileRegister.nextID fire gate.'
 # 0.9.3: the v1.1 routes exist in the built DLL, not only in the pure core.
 $runtime=$a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.HingeRuntime'
 $saveCalls=CallsOf $runtime 'Save'
 if(!($saveCalls | Where-Object { $_ -match 'IoApiCore::PartDataJson' })){ throw 'Save must read per-part API data.' }
 if(!($saveCalls | Where-Object { $_ -match 'ExternalPartState::WriteData' })){ throw 'Save must merge ioPartDataV1.' }
 $loadCalls=CallsOf $runtime 'Load'
 if(!($loadCalls | Where-Object { $_ -match 'HingeRuntime::LoadExternalData' })){ throw 'Load must read per-part API data.' }
 if(!(CallsOf $runtime 'LoadExternalData' | Where-Object { $_ -match 'ExternalPartState::ReadData' })){ throw 'LoadExternalData must use the state reader.' }
 foreach($name in @('Designer','Remove')) { if(!(CallsOf $runtime $name | Where-Object { $_ -match 'HingeRuntime::TurnOffExternal' })){ throw "$name must turn a still-on external output off." } }
 if(!(CallsOf $runtime 'TurnOffExternal' | Where-Object { $_ -match 'ExternalOutputPower::TurnOff' })){ throw 'M4 must use the one-shot power gate.' }
 if(!(CallsOf $runtime 'SaveFlow' | Where-Object { $_ -match 'ExternalPartState::WriteFlow' })){ throw 'Managed save must write ioFlowV1/ioPartId on the part.' }
 if(!(CallsOf $runtime 'FlowComponent' | Where-Object { $_ -match 'IoApiCore::IsRegisteredComponent' })){ throw 'LoadData filter must admit registered external ComponentIDs.' }
 'PASS v1.1 routes in the built DLL: per-part data on Save/Load, managed flow in SaveFlow, M4 one-shot off on designer/release, external ComponentIDs in the LoadData filter.'
 $plugin=$a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.Plugin'
 $info=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' })
 if($info.Count -ne 1 -or $info[0].ConstructorArguments[0].Value -ne 'sprocket.telescopicmast' -or $info[0].ConstructorArguments[2].Value -ne '0.9.9') { throw 'Built DLL identity/version mismatch.' }
 # IO-API.md §2: exact public facade signatures, checked in the built managed assembly.
 $api=$a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.Io.IoApi'
 if(!$api -or !$api.IsPublic -or !$api.IsAbstract -or !$api.IsSealed) { throw 'IoApi must be a public static class.' }
 $versionField=$api.Fields | Where-Object Name -eq 'ApiVersion'
 if(!$versionField -or !$versionField.IsLiteral -or [int]$versionField.Constant -ne 1) { throw 'IoApi.ApiVersion must be const int 1.' }
 $expected=@{
  'RegisterLevelInput'='System.Boolean|System.String,System.String,System.String,System.Func`2<UnityEngine.GameObject,System.Boolean>'
  'RegisterEventInput'='System.Boolean|System.String,System.String,System.String,System.Func`2<UnityEngine.GameObject,System.Int32>'
  'RegisterOutput'='System.Boolean|System.String,System.String,System.String,System.Action`2<UnityEngine.GameObject,System.Boolean>,System.Func`2<UnityEngine.GameObject,System.String>,System.Action`2<UnityEngine.GameObject,System.String>,System.String,System.String'
 }
 foreach($name in $expected.Keys) {
  $methods=@($api.Methods | Where-Object { $_.IsPublic -and $_.IsStatic -and $_.Name -eq $name })
  if($methods.Count -ne 1) { throw "IoApi.$name must have exactly one public static overload." }
  $signature=$methods[0].ReturnType.FullName + '|' + (($methods[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',')
  if($signature -cne $expected[$name]) { throw "IoApi.$name signature differs from IO-API.md §2: $signature" }
 }
 $out=$api.Methods | Where-Object { $_.IsPublic -and $_.Name -eq 'RegisterOutput' }
 if($out.Parameters[6].Name -ne 'onLabel' -or $out.Parameters[6].Constant -ne 'On' -or $out.Parameters[7].Name -ne 'offLabel' -or $out.Parameters[7].Constant -ne 'Off') { throw 'RegisterOutput labels must default to On/Off.' }
 # v1.1: additive methods from IO-API.md §6; ApiVersion stays 1.
 $revision=$api.Fields | Where-Object Name -eq 'ApiRevision'
 if(!$revision -or !$revision.IsLiteral -or [int]$revision.Constant -ne 2) { throw 'IoApi.ApiRevision must be const int 2.' }
 $v11=@{
  'SetPartData'='System.Boolean|UnityEngine.GameObject,System.String,System.String'
  'GetPartData'='System.String|UnityEngine.GameObject,System.String'
  'RegisterManagedOutput'='System.Boolean|System.String,System.String,System.String,System.Action`2<UnityEngine.GameObject,System.Boolean>,System.String,System.String'
  'RegisterPartIcon'='System.Boolean|System.String,System.String'
  'RegisterPartSettings'='System.Boolean|System.String,System.String,System.String,System.Action`1<UnityEngine.GameObject>'
 }
 foreach($name in $v11.Keys) {
  $methods=@($api.Methods | Where-Object { $_.IsPublic -and $_.IsStatic -and $_.Name -eq $name })
  if($methods.Count -ne 1) { throw "IoApi.$name must have exactly one public static overload." }
  $signature=$methods[0].ReturnType.FullName + '|' + (($methods[0].Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',')
  if($signature -cne $v11[$name]) { throw "IoApi.$name signature differs from IO-API.md §6: $signature" }
 }
 $settings=$api.Methods | Where-Object { $_.IsPublic -and $_.Name -eq 'RegisterPartSettings' }
 if($settings.Parameters[3].Name -ne 'onChanged' -or !$settings.Parameters[3].IsOptional -or $settings.Parameters[3].Constant -ne $null){throw 'RegisterPartSettings must have optional onChanged = null.'}
 $managed=$api.Methods | Where-Object { $_.IsPublic -and $_.Name -eq 'RegisterManagedOutput' }
 if($managed.Parameters[4].Name -ne 'onLabel' -or $managed.Parameters[4].Constant -ne 'On' -or $managed.Parameters[5].Name -ne 'offLabel' -or $managed.Parameters[5].Constant -ne 'Off') { throw 'RegisterManagedOutput labels must default to On/Off.' }
 if($a.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.Io.IoApiClient') { throw 'api/IoApiClient.cs must not be compiled into the mast DLL.' }
 if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'api/IoApiClient.cs'))) { throw 'api/IoApiClient.cs is missing.' }
 'PASS IoApi.ApiVersion/ApiRevision and the three v1 + four v1.1 + v1.2 settings signatures in the built DLL; client shim is not compiled in.'
 # 0.7.0: the I/O panel is a UI Kit panel. Hard dependency, kit referenced (not shipped), no own IMGUI/cursor/input-block/pointer code.
 $deps=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' })
 $kitDep=@($deps | Where-Object { $_.ConstructorArguments[0].Value -eq 'sprocket.uikit' })
 if($kitDep.Count -ne 1 -or $kitDep[0].ConstructorArguments[1].Value -ne '>=0.1.12 <0.2.0') { throw 'Missing or wrong UI Kit dependency (sprocket.uikit, >=0.1.12 <0.2.0).' }
 if(@($a.MainModule.AssemblyReferences | Where-Object Name -eq 'SprocketUiKit').Count -ne 1) { throw 'Built DLL does not reference SprocketUiKit.' }
 foreach($gone in @('SprocketTelescopicMast.IoPanel','SprocketTelescopicMast.HingeInputBlock','SprocketTelescopicMast.HingePointerHooks')) { if($a.MainModule.Types | Where-Object FullName -eq $gone) { throw "$gone must stay removed (the UI Kit owns panels, cursor and input block)." } }
 foreach($type in $a.MainModule.Types) { foreach($method in $type.Methods) {
  if($method.Name -eq 'OnGUI') { throw "IMGUI OnGUI left in $($type.FullName)" }
  if(!$method.HasBody) { continue }
  foreach($i in $method.Body.Instructions) { if($i.Operand -is [Mono.Cecil.MethodReference]) {
   $n=$i.Operand.FullName
   if($n -match 'UnityEngine\.(GUI|GUILayout|GUIStyle)::|UnityEngine\.Cursor::set_|Sprocket\.Inputsystem\.Inputs::set_') { throw "Own panel/cursor/input code left in $($method.FullName): $n" }
  }}
 }}
 $calls=@(foreach($type in $a.MainModule.Types){foreach($m in $type.Methods){if($m.HasBody){foreach($i in $m.Body.Instructions){if($i.Operand -is [Mono.Cecil.MethodReference]){$i.Operand.FullName}}}}})
 foreach($api in @('SprocketUiKit.UiKit::CreatePanel','SprocketUiKit.UiKit::BindToggle')) { if(!($calls | Where-Object { $_ -match [regex]::Escape($api) })) { throw "Panel does not call $api" } }
 'PASS UI Kit dependency >=0.1.12 <0.2.0, kit referenced, CreatePanel/BindToggle used; no IoPanel, IMGUI, cursor, pointer-guard or input-block code left.'
} finally { $a.Dispose() }
$kitPath=Join-Path $GameRef 'BepInEx/plugins/SprocketUiKit/SprocketUiKit.dll'
if(!(Test-Path -LiteralPath $kitPath)) { throw 'Sprocket UI Kit is not available (BepInEx/plugins/SprocketUiKit/SprocketUiKit.dll under -GameRef): the I/O panel requires it.' }
$kit=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($kitPath)
try {
 $kv=$kit.Name.Version
 if($kv -lt [version]'0.1.12.0' -or $kv -ge [version]'0.2.0.0') { throw "Installed UI Kit $kv is outside >=0.1.12 <0.2.0." }
 $ui=$kit.MainModule.Types | Where-Object FullName -eq 'SprocketUiKit.UiKit'
 if(($ui.Fields | Where-Object Name -eq 'PluginGuid').Constant -ne 'sprocket.uikit') { throw 'UI Kit GUID changed.' }
 foreach($m in @('CreatePanel','BindToggle','DestroyPanel','get_TextInputActive','get_Context')) { if(!($ui.Methods | Where-Object Name -eq $m)) { throw "UI Kit API missing: UiKit::$m" } }
 "PASS UI Kit $kv installed with the API the panel uses."
} finally { $kit.Dispose() }
[xml]$proj=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'SprocketTelescopicMast.csproj')
$kitRef=@($proj.Project.ItemGroup.Reference | Where-Object Include -eq 'SprocketUiKit')
if($kitRef.Count -ne 1 -or $kitRef[0].Private -ne 'false') { throw 'SprocketUiKit must be referenced with Private=false.' }
if(Test-Path (Join-Path (Split-Path $dll -Parent) 'SprocketUiKit.dll')) { throw 'Do not ship a UI Kit copy with the mast.' }
if((Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'README.md')) -notmatch 'UI Kit') { throw 'README must say the Sprocket UI Kit is required.' }
'PASS kit referenced Private=false, not shipped with the mast, README names the requirement.'
'PASS game hash, part identity, hinge parts, mounting surfaces, hook signatures and built callbacks. This does not validate native movement, firing hooks, the panel or saving in game.'

# Approved icons: read-only house-style validation; never regenerate here.
$approvedIcons=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'tools/approved-icons.sha256.json') | ConvertFrom-Json
foreach($name in @('telescope-base','telescope-part','hinge-base','hinge-leaf','hinge-mount','rotator-base','rotator-plate')) {
 & $IconChecker -Check (Join-Path $PSScriptRoot ('assets/'+$name+'-icon.png'))
 $approved=$approvedIcons.($name+'-icon.png')
 if($approved -ine (Get-FileHash (Join-Path $PSScriptRoot ('assets/'+$name+'-icon.png'))).Hash) {throw "Approved icon changed: $name"}
}
'PASS all seven approved icons: house style and unchanged bytes.'
