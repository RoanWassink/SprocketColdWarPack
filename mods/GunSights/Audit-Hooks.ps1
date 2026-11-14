# Checks that every [HarmonyPatch] target of Sprocket Gun Sights exists exactly once in the game's interop
# assemblies. Reads only (game folder and the built DLL); writes the result only when -OutFile is given.
# Use -GameDir <copy of BepInEx\core + interop> to run it without the real game folder.
param(
 [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
 [string]$Dll=(Join-Path $PSScriptRoot 'src\bin\Release\net6.0\SprocketGunSights.dll'),
 [string]$OutFile=''
)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')
$types=@{};$assemblies=[System.Collections.Generic.List[object]]::new()
Get-ChildItem (Join-Path $GameDir 'BepInEx\interop\Sprocket*.dll')|ForEach-Object {
 $a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($_.FullName);$assemblies.Add($a)
 foreach($t in $a.MainModule.Types){$types[$t.FullName]=$t}
}
$plugin=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll)
$result=@()
try {
 foreach($name in @('SightChoices','ScopeRuntime')){
  $type=$plugin.MainModule.Types|Where-Object Name -eq $name
  foreach($m in $type.Methods){
   $patches=@($m.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch'})
   if($patches.Count -gt 1){throw ('Ambiguous multiple HarmonyPatch method metadata: '+$m.FullName)}
   foreach($patch in $patches){
    if($patch.ConstructorArguments.Count -notin 2,3){throw ('Unexpected patch constructor: '+$m.FullName)}
    $target=$patch.ConstructorArguments[0].Value.FullName;$method=[string]$patch.ConstructorArguments[1].Value
    # Optional third argument: exact parameter types selecting one overload.
    $sig=$null;if($patch.ConstructorArguments.Count -eq 3){$sig=@($patch.ConstructorArguments[2].Value|ForEach-Object {$_.Value.FullName})}
    $match={param($x) $x.Name -eq $method -and ($null -eq $sig -or (($x.Parameters|ForEach-Object {$_.ParameterType.FullName}) -join ',') -eq ($sig -join ','))}
    $t=$types[$target];if(!$t){throw ('Missing native type: '+$target)}
    $found=@($t.Methods|Where-Object {& $match $_})
    while($found.Count -eq 0 -and $t.BaseType -and $types.ContainsKey($t.BaseType.FullName)){$t=$types[$t.BaseType.FullName];$found=@($t.Methods|Where-Object {& $match $_})}
    if($found.Count -ne 1){throw ('Native target missing or ambiguous: '+$target+'::'+$method)}
    $result+=[pscustomobject]@{patch=$m.Name;requestedType=$target;nativeSignature=$found[0].FullName;declaredOn=$t.FullName}
   }
  }
 }
 if($OutFile){$result|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $OutFile}
 "PASS $($result.Count) unique native Harmony targets; IL2CPP patch execution remains unverified."
} finally {$plugin.Dispose();foreach($a in $assemblies){$a.Dispose()}}
