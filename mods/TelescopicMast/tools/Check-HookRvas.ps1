param(
 [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
 # Optional: an all-native-methods.json index of an earlier dump to compare RVAs/lengths against. Without it only uniqueness is checked.
 [string]$ReferenceIndex=''
)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
$targets=@(
 'Sprocket.Vehicles.VehicleObjectModel::Build',
 'Sprocket.Vehicles.VehicleObjectModel::OnModelLoaded',
 'Sprocket.Vehicles.VehicleObject::EnablePhysics',
 'Sprocket.Vehicles.VehicleObject::DisablePhysics',
 'Sprocket.Vehicles.VehicleTransform::SaveState',
 'Sprocket.Vehicles.VehicleObjectSerialization::ToBlueprint',
 'Sprocket.Vehicles.VehicleComponent::LoadData',
 'Sprocket.Vehicles.VehicleObject::Release',
 'Sprocket.Vehicles.VehicleTransform::LoadState',
 'Sprocket.Vehicles.Weapons.Cannons.CannonBehaviour::FireInternal',
 'Sprocket.Vehicles.Cannons.Cannon::Build',
 'Sprocket.Vehicles.Cannons.Cannon::OnVehicleBuilt',
 'Sprocket.Vehicles.Cannons.Cannon::Release',
 'Sprocket.PartImporting.PartDefinitionCardFactory::CreateCard'
)

$rows=[System.Collections.Generic.List[object]]::new()
$root=Join-Path $GameDir 'MLLoader/MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out'
if(!(Test-Path $root)){ throw "Current Cpp2IL output not found: $root" }
foreach($asm in Get-ChildItem $root -Filter 'Sprocket*.dll'){
 $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($asm.FullName)
 function Walk($types){ foreach($t in $types){
   foreach($m in $t.Methods){
     $at=$m.CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'AddressAttribute'}
     if($at){ $f=@{}; foreach($x in $at.Fields){$f[$x.Name]=$x.Argument.Value}; $rows.Add([pscustomobject]@{Name=$m.FullName;RVA=$f.RVA;Length=$f.Length}) }
   }
   if($t.HasNestedTypes){ Walk $t.NestedTypes }
 }}
 try { Walk $a.MainModule.Types } finally {$a.Dispose()}
}
$old=@()
if($ReferenceIndex -and (Test-Path $ReferenceIndex)){ $old=Get-Content -Raw $ReferenceIndex | ConvertFrom-Json }
$bad=0
foreach($t in $targets){
 $hit=@($rows | Where-Object {$_.Name.Contains($t+'(')})
 if($hit.Count -ne 1){ "FAIL current target $t count=$($hit.Count)"; $bad++; continue }
 $h=$hit[0]
 $shared=@($rows | Where-Object {$_.RVA -eq $h.RVA -and $_.Name -ne $h.Name})
 $oldHit=@($old | Where-Object {$_.Name.Contains($t+'(')})
 $same='n/a'
 if($oldHit.Count -eq 1){ $same=($oldHit[0].RVA -eq $h.RVA -and $oldHit[0].Length -eq $h.Length) }
 "$($h.Name) | RVA=$($h.RVA) | Len=$($h.Length) | shared=$($shared.Count) | referenceIndexMatches=$same"
 foreach($s in $shared){ "  SHARED $($s.Name) Len=$($s.Length)" }
 if($shared.Count -gt 0 -or ($old.Count -gt 0 -and ($oldHit.Count -ne 1 -or !$same))){ $bad++ }
}
$hash=(Get-FileHash -Algorithm SHA256 (Join-Path $GameDir 'GameAssembly.dll')).Hash
"GameAssembly SHA256 $hash"
if($hash -ne '3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B'){ "FAIL unexpected game binary"; $bad++ }
if($bad -gt 0){ "FAIL $bad hook-address checks"; exit 1 }
"PASS $($targets.Count) unique hook targets; current Cpp2IL RVAs share no other method$(if($old.Count -gt 0){' and match the reference index'})."
