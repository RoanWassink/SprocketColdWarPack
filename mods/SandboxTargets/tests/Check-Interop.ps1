param([string]$GameRoot='C:\Program Files (x86)\Steam\steamapps\common\Sprocket')
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx\core\Mono.Cecil.dll')
$checks=0
function Assert-Contract([bool]$Condition,[string]$Name){
    if(!$Condition){throw "FAIL: $Name"}
    $script:checks++
    Write-Output "PASS: $Name"
}
$assemblies=@{}
foreach($name in @('Sprocket.Vehicles.Missions','Sprocket.ScenarioGameMode','Sprocket.Vehicle.Spawning','Sprocket.Vehicles','Sprocket.VehicleFramework','Sprocket.Motors','Sprocket.MissionFramework')){
    $assemblies[$name]=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot "BepInEx\interop\$name.dll"))
}
try{
    $stage=$assemblies['Sprocket.Vehicles.Missions'].MainModule.Types | Where-Object FullName -eq 'Sprocket.Vehicles.Missions.StageVehicleSpawnEvent'
    $reset=@($stage.Methods | Where-Object {$_.Name -eq 'StageReset' -and $_.Parameters.Count -eq 0})
    Assert-Contract ($reset.Count -eq 1 -and $reset[0].ReturnType.FullName -eq 'Il2CppSystem.Threading.Tasks.Task') 'StageReset: parameterless Task (safe hook signature)'
    Assert-Contract (@($stage.Methods | Where-Object {$_.Name -eq 'GetSpawnInstances' -and $_.Parameters.Count -eq 0}).Count -eq 1) 'GetSpawnInstances: parameterless (hooked)'
    $start=@($stage.Methods | Where-Object {$_.Name -eq 'StageStart' -and $_.Parameters.Count -eq 1})
    Assert-Contract ($start.Count -eq 1 -and $start[0].Parameters[0].ParameterType.FullName -eq 'Il2CppSystem.Threading.CancellationToken') 'StageStart(CancellationToken): called, not hooked'
    $event=$assemblies['Sprocket.MissionFramework'].MainModule.Types | Where-Object FullName -eq 'Sprocket.MissionFramework.StageEvent'
    Assert-Contract (@($event.Methods | Where-Object {$_.Name -eq 'Start' -and $_.Parameters.Count -eq 0}).Count -eq 1) 'StageEvent.Start: parameterless (not hooked)'
    $mission=$assemblies['Sprocket.ScenarioGameMode'].MainModule.Types | Where-Object FullName -eq 'Sprocket.MissionScenarioGameState'
    Assert-Contract (@($mission.Methods | Where-Object {$_.Name -eq 'Exit' -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'Il2CppSystem.Threading.Tasks.Task'}).Count -eq 1) 'Mission Exit: parameterless Task (safe hook signature)'
    $spawner=$assemblies['Sprocket.Vehicle.Spawning'].MainModule.Types | Where-Object FullName -eq 'Sprocket.Vehicles.Spawning.VehicleSpawner'
    $spawn=@($spawner.Methods | Where-Object {$_.Name -eq 'Spawn' -and $_.Parameters.Count -eq 4 -and $_.Parameters[1].ParameterType.FullName -like '*Il2CppStructArray*VehicleSpawnInstance*'})
    Assert-Contract ($spawn.Count -eq 1) 'Spawn: actual native array overload resolves'
    Assert-Contract ($spawn[0].Parameters[3].ParameterType.FullName -eq 'Il2CppSystem.Threading.CancellationToken') 'Native cancellation token type'
    $source=$assemblies['Sprocket.Vehicle.Spawning'].MainModule.Types | Where-Object FullName -eq 'Sprocket.Vehicles.Spawning.VehicleSource'
    Assert-Contract (@($source.Methods | Where-Object {$_.Name -eq 'GetBlueprint' -and $_.Parameters.Count -eq 0}).Count -eq 1) 'Stock source GetBlueprint resolves'
    foreach($p in @('Mode','Source','Subdirectory','BlueprintName')){Assert-Contract (@($source.Properties | Where-Object {$_.Name -eq $p -and $_.SetMethod}).Count -eq 1) "VehicleSource.$p settable"}
    Assert-Contract (@($source.Methods | Where-Object {$_.Name -eq 'GetPath' -and $_.Parameters.Count -eq 0}).Count -eq 1) 'VehicleSource.GetPath resolves'
    $serializer=$assemblies['Sprocket.Vehicles'].MainModule.Types | Where-Object FullName -eq 'Sprocket.Vehicles.VehicleBlueprintSerializer'
    if(!$serializer){$serializer=$assemblies['Sprocket.VehicleFramework'].MainModule.Types | Where-Object FullName -eq 'Sprocket.Vehicles.VehicleBlueprintSerializer'}
    Assert-Contract (@($serializer.Methods | Where-Object {$_.Name -eq 'LoadFromPath' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.String'}).Count -eq 1) 'Native blueprint path loader resolves'
    $turret=$assemblies['Sprocket.Motors'].MainModule.Types | Where-Object FullName -eq 'Sprocket.Vehicles.Weapons.TurretBehaviour'
    Assert-Contract (@($turret.Methods | Where-Object {$_.Name -eq 'ResetRotation' -and $_.Parameters.Count -eq 0}).Count -eq 1) 'Native turret ResetRotation resolves'
    $pluginPath=Join-Path $PSScriptRoot '..\src\bin\Release\net6.0\SprocketSandboxTargets.dll'
    $plugin=[Mono.Cecil.AssemblyDefinition]::ReadAssembly([IO.Path]::GetFullPath($pluginPath))
    try{
        Assert-Contract ($plugin.Name.Version.ToString() -eq '0.3.2.0') 'Built assembly version 0.3.2.0'
        $type=$plugin.MainModule.Types | Where-Object FullName -eq 'SprocketSandboxTargets.Plugin'
        $attr=$type.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'}
        Assert-Contract ($attr.ConstructorArguments[0].Value -eq 'sprocket.sandboxtargets' -and $attr.ConstructorArguments[2].Value -eq '0.3.2') 'Built plugin identity/version'
        $patches=@($plugin.MainModule.Types | ForEach-Object {$_.CustomAttributes} | Where-Object {$_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch'})
        Assert-Contract ($patches.Count -eq 3) 'Three patch classes: GetSpawnInstances, StageReset, Exit'
    }finally{$plugin.Dispose()}
    Write-Output "PASS: $checks interop/build contracts. Metadata check only; no native execution."
}finally{foreach($a in $assemblies.Values){$a.Dispose()}}
