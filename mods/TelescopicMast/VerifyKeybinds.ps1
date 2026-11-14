# Checks the Keybinds integration of the built DLL. Writes nothing.
#  -GameDir  the Sprocket game folder (READ ONLY, Mono.Cecil); -GameRef  folder with BepInEx\plugins\SprocketKeybinds (default: GameDir).
param(
 [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
 [string]$GameRef='',
 [string]$DllPath=''
)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($GameRef)){ $GameRef=$GameDir }
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
$apiPath=Join-Path $GameRef 'BepInEx/plugins/SprocketKeybinds/SprocketKeybinds.dll'
$api=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($apiPath)
try {
 $version=$api.Name.Version
 if($version -lt [version]'0.1.3.0' -or $version -ge [version]'0.2.0.0'){throw "Unsupported Keybinds API $version"}
 foreach($pair in @(@('SprocketKeybinds.Keybinds','RegisterButton'),@('SprocketKeybinds.Keybinds','TryImportLegacyBinding'),@('SprocketKeybinds.Keybinds','Unregister'),@('SprocketKeybinds.Keybinds','get_IsConfiguring'),@('SprocketKeybinds.ModKeybind','get_IsHeld'))) {
  $type=$api.MainModule.Types | Where-Object FullName -eq $pair[0]
  if(!($type.Methods | Where-Object Name -eq $pair[1])){throw "API method missing: $($pair[0])::$($pair[1])"}
 }
 "PASS shared Keybinds API $version public entry points."
} finally {$api.Dispose()}
$dll=if([string]::IsNullOrWhiteSpace($DllPath)){Join-Path $PSScriptRoot 'bin/Release/net6.0/SprocketTelescopicMast.dll'}else{$DllPath}
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
 $plugin=$assembly.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.Plugin'
 $dependency=@($plugin.CustomAttributes | Where-Object AttributeType -ne $null | Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInDependency'})
 $info=@($plugin.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'})
 if($info.Count -ne 1 -or $info[0].ConstructorArguments[2].Value -ne '0.9.9'){throw 'DLL is not 0.9.9; build/copy the fresh 0.9.9 DLL first.'}
 $kb=@($dependency | Where-Object {$_.ConstructorArguments[0].Value -eq 'sprocket.keybinds'})
 if($dependency.Count -ne 2 -or $kb.Count -ne 1 -or $kb[0].ConstructorArguments[1].Value -ne '>=0.1.3 <0.2.0'){throw 'Incorrect Keybinds dependency range (expected Keybinds + UI Kit dependencies)'}
 $runtime=$assembly.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.MastRuntime'
 $update=$runtime.Methods | Where-Object Name -eq 'Update'
 $calls=@($update.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.FullName})
 if(@($calls | Where-Object {$_ -match 'SprocketKeybinds.ModKeybind::get_IsHeld'}).Count -ne 2){throw 'Expected continuous Raise/Lower API polling'}
# 0.6.0: no exceptions. The UI Kit owns mouse, typing and the panel toggle; the 0.5 IoPanel polling exception is gone.
 foreach($type in $assembly.MainModule.Types){foreach($method in $type.Methods){if($method.HasBody){foreach($instruction in $method.Body.Instructions){if($instruction.Operand -is [Mono.Cecil.MethodReference] -and $instruction.Operand.FullName -cmatch '::get_(isPressed|wasPressedThisFrame|wasReleasedThisFrame)\('){throw "Direct button polling found: $($method.FullName)"}}}}}
 if(@($assembly.MainModule.AssemblyReferences | Where-Object Name -eq 'SprocketKeybinds').Count -ne 1){throw 'Missing canonical API reference'}
 'PASS dependency >=0.1.3 <0.2.0 and two IsHeld calls; no direct button polling.'
}finally{$assembly.Dispose()}
[xml]$project=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'SprocketTelescopicMast.csproj')
$reference=@($project.Project.ItemGroup.Reference | Where-Object Include -eq 'SprocketKeybinds')
if($reference.Count -ne 1 -or $reference[0].Private -ne 'false'){throw 'Shared API must be referenced Private=false'}
if(Test-Path (Join-Path (Split-Path $dll -Parent) 'SprocketKeybinds.dll')){throw 'Do not distribute an API copy inside the mast plugin'}
foreach($name in @('telescopeBase','telescopePart','telescopicSightMast','hingeBase','hingeLeaf','hingeMount')){
 [xml]$xml=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "assets/$name.xml")
 if($xml.Part.description -notmatch 'Settings keybind menu' -or $xml.Part.description -match 'Hold Insert|Hold Delete|Insert/Delete'){throw "Obsolete fixed-key tooltip $name"}
}
'PASS Private=false, no bundled API and all six control tooltips point to Settings.'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
 $strings=@(foreach($t in $assembly.MainModule.Types){foreach($m in $t.Methods){if($m.HasBody){foreach($i in $m.Body.Instructions){if($i.OpCode.Code -eq 'Ldstr'){$i.Operand}}}}})
 foreach($needed in @('hinge-panel','I/O Flow / Open panel','<Keyboard>/f8','hinge-action-','I/O Flow / Action ','<Keyboard>/numpad')){ if($strings -notcontains $needed){throw "Missing hinge keybind registration string: $needed"} }
 $keys=$assembly.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.HingeKeys'
 $poll=$keys.Methods | Where-Object Name -eq 'Poll'
 $calls=@($poll.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.FullName})
 if(@($calls | Where-Object {$_ -match 'SprocketKeybinds.ModKeybind::get_(IsHeld|WasPressedThisFrame)'}).Count -ne 2){throw 'Hinge action keys must be polled through the Keybinds API'}
 $create=($assembly.MainModule.Types | Where-Object FullName -eq 'SprocketTelescopicMast.HingePanel').Methods | Where-Object Name -eq 'Create'
 $createCalls=@($create.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.FullName})
 if(@($createCalls | Where-Object {$_ -match 'SprocketUiKit.UiKit::BindToggle\(SprocketUiKit.Panel,SprocketKeybinds.ModKeybind\)'}).Count -ne 1){throw 'The I/O panel must toggle through UiKit.BindToggle with the I/O Flow / Open panel keybind'}
 $load=($plugin.Methods | Where-Object Name -eq 'LoadHinge').Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -or $_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.FullName}
 if(!(@($load) -match 'PanelBinding') -or !(@($load) -match 'HingePanel::Create')){throw 'LoadHinge must hand the PanelBinding (F8) to HingePanel.Create'}
 'PASS hinge keybinds registered through the API (I/O panel F8 via UiKit.BindToggle, Action 1-4 Numpad 1-4 factory defaults) and polled via ModKeybind; no direct key/mouse polling anywhere.'
} finally { $assembly.Dispose() }
