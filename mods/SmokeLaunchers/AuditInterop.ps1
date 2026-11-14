param([string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket', [string]$Dll=(Join-Path $PSScriptRoot 'bin/Release/net6.0/SprocketSmokeLaunchers.dll'))
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
$resolverTask=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolverTask.AddSearchDirectory((Join-Path $GameDir 'BepInEx/interop')); $resolverTask.AddSearchDirectory((Join-Path $GameDir 'BepInEx/core'))
$paramsTask=[Mono.Cecil.ReaderParameters]::new(); $paramsTask.AssemblyResolver=$resolverTask
$assemblyTask=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll,$paramsTask)
$queueTask=[Collections.Generic.Queue[Mono.Cecil.MethodReference]]::new()
$seenTask=[Collections.Generic.HashSet[string]]::new()
try {
 if($assemblyTask.MainModule.AssemblyReferences | Where-Object Name -EQ 'UnityEngine.ParticleSystemModule') { throw 'Removed backend still referenced' }
 foreach($typeTask in $assemblyTask.MainModule.Types) { foreach($methodTask in $typeTask.Methods) {
  if(!$methodTask.HasBody) { continue }
  foreach($insTask in $methodTask.Body.Instructions) {
   if($insTask.Operand -is [Mono.Cecil.MethodReference] -and $insTask.Operand.DeclaringType.FullName -like 'UnityEngine.*') { $queueTask.Enqueue($insTask.Operand) }
  }
 }}
 while($queueTask.Count -gt 0) {
  $referenceTask=$queueTask.Dequeue()
  if(!$seenTask.Add($referenceTask.FullName)) { continue }
  $resolvedTask=$referenceTask.Resolve()
  if(!$resolvedTask) { throw "Unresolved actual interop member $($referenceTask.FullName)" }
  if(!$resolvedTask.HasBody -and $resolvedTask.Name -eq "Invoke" -and $resolvedTask.DeclaringType.BaseType.FullName -eq "System.MulticastDelegate") { "PASS native icall delegate endpoint $($referenceTask.FullName)"; continue }
  if(!$resolvedTask.HasBody) { throw "No inspectable interop body $($referenceTask.FullName)" }
  foreach($insTask in $resolvedTask.Body.Instructions) {
   if($insTask.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr -and "$($insTask.Operand)" -match 'unstripping failed') { throw "Uncallable stub reached: $($referenceTask.FullName)" }
   if($insTask.Operand -is [Mono.Cecil.MethodReference] -and $insTask.Operand.DeclaringType.FullName -like 'UnityEngine.*') { $queueTask.Enqueue($insTask.Operand) }
  }
 }
 "PASS $($seenTask.Count) direct/transitive Unity interop bodies resolved and audited: no unstripping-failed stub reachable from plugin calls."
 'PASS consumer has no ParticleSystemModule reference; old scalar setter and emitter dependency entirely removed.'
 'This checks generated bodies, not runtime rendering; native retest is required.'
} finally { $assemblyTask.Dispose(); $resolverTask.Dispose() }
