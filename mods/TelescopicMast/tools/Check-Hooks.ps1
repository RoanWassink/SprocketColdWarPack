param(
 [string]$DllPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'bin\Release\net6.0\SprocketTelescopicMast.dll'),
 [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sprocket'   # READ ONLY (Mono.Cecil)
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
if(!(Test-Path -LiteralPath $DllPath)){ throw "DLL not found: $DllPath" }

# 0.7.1 hook set: 24 callbacks on 14 targets. The per-vehicle GameUpdate/UpdateControl hooks and
# both ProjectileRegister.Launch hooks are gone; FireInternal has a prefix and a postfix.
$expected = @(
 'SprocketTelescopicMast.HingeCoreHooks|BeforeBuild|Prefix|Sprocket.Vehicles.VehicleObjectModel|Build',
 'SprocketTelescopicMast.HingeCoreHooks|AfterBuild|Postfix|Sprocket.Vehicles.VehicleObjectModel|Build',
 'SprocketTelescopicMast.HingeCoreHooks|Loaded|Postfix|Sprocket.Vehicles.VehicleObjectModel|OnModelLoaded',
 'SprocketTelescopicMast.HingeCoreHooks|Physics|Prefix|Sprocket.Vehicles.VehicleObject|EnablePhysics',
 'SprocketTelescopicMast.HingeCoreHooks|Designer|Prefix|Sprocket.Vehicles.VehicleObject|DisablePhysics',
 'SprocketTelescopicMast.HingeCoreHooks|SaveState|Prefix|Sprocket.Vehicles.VehicleTransform|SaveState',
 'SprocketTelescopicMast.HingeCoreHooks|BeforeBlueprint|Prefix|Sprocket.Vehicles.VehicleObjectSerialization|ToBlueprint',
 'SprocketTelescopicMast.HingeCoreHooks|Save|Postfix|Sprocket.Vehicles.VehicleObjectSerialization|ToBlueprint',
 'SprocketTelescopicMast.HingeCoreHooks|LoadData|Postfix|Sprocket.Vehicles.VehicleComponent|LoadData',
 'SprocketTelescopicMast.HingeCoreHooks|Release|Prefix|Sprocket.Vehicles.VehicleObject|Release',
 'SprocketTelescopicMast.HingeLoadBackupHooks|LoadState|Postfix|Sprocket.Vehicles.VehicleTransform|LoadState',
 'SprocketTelescopicMast.HingeFireHooks|BeforeFire|Prefix|Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour|FireInternal',
 'SprocketTelescopicMast.HingeFireHooks|AfterFire|Postfix|Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour|FireInternal',
 'SprocketTelescopicMast.HingeFireHooks|Built|Postfix|Sprocket.Vehicles.Cannons.Cannon|Build',
 'SprocketTelescopicMast.HingeFireHooks|VehicleBuilt|Postfix|Sprocket.Vehicles.Cannons.Cannon|OnVehicleBuilt',
 'SprocketTelescopicMast.HingeFireHooks|Released|Prefix|Sprocket.Vehicles.Cannons.Cannon|Release',
 'SprocketTelescopicMast.Hooks|BeforeBuild|Prefix|Sprocket.Vehicles.VehicleObjectModel|Build',
 'SprocketTelescopicMast.Hooks|AfterBuild|Postfix|Sprocket.Vehicles.VehicleObjectModel|Build',
 'SprocketTelescopicMast.Hooks|Loaded|Postfix|Sprocket.Vehicles.VehicleObjectModel|OnModelLoaded',
 'SprocketTelescopicMast.Hooks|Designer|Prefix|Sprocket.Vehicles.VehicleObject|DisablePhysics',
 'SprocketTelescopicMast.Hooks|Physics|Prefix|Sprocket.Vehicles.VehicleObject|EnablePhysics',
 'SprocketTelescopicMast.Hooks|Save|Prefix|Sprocket.Vehicles.VehicleTransform|SaveState',
 'SprocketTelescopicMast.Hooks|Release|Prefix|Sprocket.Vehicles.VehicleObject|Release',
 'SprocketTelescopicMast.IconHooks|Card|Postfix|Sprocket.PartImporting.PartDefinitionCardFactory|CreateCard'
)
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($DllPath)
try {
 $actual=[System.Collections.Generic.List[string]]::new()
 $remaining=[System.Collections.Generic.HashSet[string]]::new([string[]]$expected)
 foreach($type in $assembly.MainModule.Types){
  foreach($method in $type.Methods){
   $attrs=@($method.CustomAttributes | Where-Object { $_.AttributeType.FullName -like 'HarmonyLib.Harmony*' })
   if($attrs.Count -eq 0){ continue }
   $patch=@($attrs | Where-Object { $_.AttributeType.Name -eq 'HarmonyPatch' })
   $kinds=@($attrs | Where-Object { $_.AttributeType.Name -match '^Harmony(Prefix|Postfix|Finalizer|Transpiler|ReversePatch|ILManipulator)$' } | ForEach-Object { $_.AttributeType.Name -replace '^Harmony','' })
   if($patch.Count -eq 0){ throw "Harmony callback without HarmonyPatch target: $($type.FullName)::$($method.Name)" }
   $targetType=$patch[0].ConstructorArguments[0].Value
   $targetMethod=$patch[0].ConstructorArguments[1].Value
   if($targetType -eq $null -or $targetMethod -eq $null){ throw "Incomplete Harmony target on $($type.FullName)::$($method.Name)" }
   $sig="$($type.FullName)|$($method.Name)|$($kinds -join ',')|$($targetType.FullName)|$targetMethod"
   $actual.Add($sig)
   if(!$remaining.Remove($sig)){ throw "Unexpected or duplicate 0.7.1 hook: $sig" }
   if($method.Parameters | Where-Object { $_.ParameterType.IsByReference }){ throw "By-ref parameter in Harmony callback $($type.FullName)::$($method.Name)" }
   foreach($p in $method.Parameters){
    if($p.ParameterType.IsValueType){ throw "Value-type parameter in Harmony callback $($type.FullName)::$($method.Name): $($p.Name)" }
   }
   if($type.Name -eq 'HingeFireHooks' -and ($method.Name -eq 'BeforeFire' -or $method.Name -eq 'AfterFire')){
    if(@($method.Parameters).Count -ne 1 -or $method.Parameters[0].Name -ne '__instance' -or $method.Parameters[0].ParameterType.FullName -ne 'Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour'){
     throw "$($method.Name) must take only CannonBehaviour __instance."
    }
   }
   if($targetType.FullName -eq 'Sprocket.Vehicles.VehicleBehaviour' -and $targetMethod -eq 'GameUpdate'){ throw 'Removed GameUpdate hook still present.' }
   if($targetType.FullName -eq 'Sprocket.Gameplay.VehicleControl.VehicleController' -and $targetMethod -eq 'UpdateControl'){ throw 'Removed UpdateControl hook still present.' }
   if($targetType.FullName -eq 'Sprocket.DamageModelling.ProjectileRegister' -and $targetMethod -eq 'Launch'){ throw 'Removed ProjectileRegister.Launch hook still present.' }
  }
 }
 if($remaining.Count -gt 0){ throw "Missing 0.7.1 hook(s): $($remaining -join '; ')" }
 "PASS 0.7.1 hook set: $($actual.Count) callbacks on $((@($expected | ForEach-Object { ($_ -split '\|')[3..4] -join '::' } | Sort-Object -Unique)).Count) targets; removed GameUpdate/UpdateControl/Launch absent; FireInternal has a prefix and a postfix, both CannonBehaviour-only."
} finally { $assembly.Dispose() }
