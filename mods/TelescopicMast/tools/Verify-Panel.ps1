param(
 [string]$DllPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'bin\Release\net6.0\SprocketTelescopicMast.dll'),
 [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sprocket',   # READ ONLY (Mono.Cecil)
 [string]$GameRef = ''                                                           # folder with BepInEx\plugins\SprocketUiKit (default: GameDir)
)
$ErrorActionPreference = 'Stop'
if([string]::IsNullOrWhiteSpace($GameRef)){ $GameRef = $GameDir }
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($DllPath)
$kit = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRef 'BepInEx/plugins/SprocketUiKit/SprocketUiKit.dll'))
try {
 if($kit.Name.Version -lt [version]'0.1.12.0' -or $kit.Name.Version -ge [version]'0.2.0.0'){ throw 'Wrong installed Kit version.' }
 function TypeKey($type) {
  if($type -is [Mono.Cecil.GenericParameter]) {
   $prefix = if($type.Type.ToString() -eq 'Method') { '!!' } else { '!' }
   return $prefix + $type.Position
  }
  if($type -is [Mono.Cecil.GenericInstanceType]) {
   return $type.ElementType.FullName + '<' + (($type.GenericArguments | ForEach-Object { TypeKey $_ }) -join ',') + '>'
  }
  return $type.FullName
 }
 $count = 0
 foreach($ref in $a.MainModule.GetMemberReferences()) {
  if($ref -isnot [Mono.Cecil.MethodReference] -or $ref.DeclaringType.Scope.Name -ne 'SprocketUiKit'){ continue }
  $method = if($ref -is [Mono.Cecil.GenericInstanceMethod]) { $ref.ElementMethod } else { $ref }
  $type = $kit.MainModule.Types | Where-Object FullName -CEQ $method.DeclaringType.GetElementType().FullName
  if(!$type -or !$type.IsPublic){ throw "Non-public Kit type: $method" }
  $match = @($type.Methods | Where-Object { $_.IsPublic -and $_.Name -ceq $method.Name -and $_.Parameters.Count -eq $method.Parameters.Count -and $_.GenericParameters.Count -eq $method.GenericParameters.Count })
  $signature = ($method.Parameters | ForEach-Object { TypeKey $_.ParameterType }) -join ','
  if(!($match | Where-Object { (($_.Parameters | ForEach-Object { TypeKey $_.ParameterType }) -join ',') -ceq $signature -and (TypeKey $_.ReturnType) -ceq (TypeKey $method.ReturnType) })) {
   throw "Kit $($kit.Name.Version) public API signature not found: $method"
  }
  $count++
 }
 "PASS $count imported Kit methods match the installed $($kit.Name.Version) public API."
 $types = @($a.MainModule.Types)
 function TypesBelow($type) { $type; foreach($nested in $type.NestedTypes){ TypesBelow $nested } }
 $all = @($types | ForEach-Object { TypesBelow $_ })
 $calls = @($all | ForEach-Object { $_.Methods } | Where-Object HasBody | ForEach-Object { $_.Body.Instructions } | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
 foreach($required in @('Container::NodeGraph','NodeGraph::FitAll','NodeGraph::BeginPendingWire','NodeGraph::EndPendingWire', 'NodeGraph::OnClick','NodeGraph::OnPortDrag','NodeGraph::PortHitRadius','NodeGraph::get_ViewSize','NodeGraph::get_VisibleRect','NodeGraph::OnNodeMoved','GraphPort::set_Chosen','GraphPort::set_On','GraphNode::set_Lamp','GraphNode::set_FooterText','GraphNode::set_Dimmed','GraphWire::set_On','Container::Segmented','Container::Stepper','Container::Collapsible','Container::Tabs','Panel::AddShortcut','FlowStorage::Publish','FlowEditor::ClickPort','FlowEditor::Drop','FlowPreview::Test')) {
  if(!($calls | Where-Object { $_ -match [regex]::Escape($required) })){ throw "Missing panel integration $required" }
 }
 'PASS retained graph, inspector, two-click wiring, storage publication and designer sandbox are wired into the DLL.'
 $signalMethod = ($all | Where-Object FullName -EQ 'SprocketTelescopicMast.HingePanel').Methods | Where-Object Name -EQ 'UpdateSignals'
 $signalCalls = @($signalMethod.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
 if($signalCalls -match '::(AddNode|AddWire|Clear|RemoveNode|RemoveWire|set_Position|set_Title|SetView|FitAll)\('){ throw 'Live refresh changes graph structure/geometry.' }
 'PASS 10 Hz signal refresh does not add/remove/reposition graph elements.'
 foreach($name in @('FlowEditor','FlowPreview')) {
  $type = $all | Where-Object FullName -EQ "SprocketTelescopicMast.Io.$name"
  if(!$type){ throw "Missing pure $name" }
 }
 $root = Split-Path $PSScriptRoot -Parent
 if(!(Test-Path -LiteralPath (Join-Path $root 'IO-API.md'))){ throw 'IO-API.md (the public I/O API contract) is missing.' }
 foreach($file in @((Get-ChildItem -LiteralPath $root -Filter '*.cs') + (Get-ChildItem -LiteralPath (Join-Path $root 'Io') -Filter '*.cs'))) {
  foreach($line in Get-Content -Encoding UTF8 -LiteralPath $file.FullName) {
   if($line.TrimStart().StartsWith('//')){continue}
   if($line -match '"' -and $line -match '[^\x00-\x7F]'){ throw "Non-ASCII code string in $($file.Name): $line" }
  }
 }
 'PASS ASCII source strings and the I/O API contract document.'
} finally { $a.Dispose(); $kit.Dispose() }
