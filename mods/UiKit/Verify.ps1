param(
 # Folder with BepInEx\core, BepInEx\interop and BepInEx\plugins\SprocketKeybinds (read only). A build copy such as herpak\gameref works.
 [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
 # GameAssembly.dll of the game (read only). Defaults to the one in GameDir.
 [string]$GameBinary='',
 # The DLL to check. Defaults to the build output of SprocketUiKit.csproj.
 [string]$Dll=''
)
# Static checks for Sprocket UI Kit: build identity, hook signatures against the game's interop,
# Harmony callback shapes, demo keybind through the Keybinds API, public API against baseline\, packaging rules.
# Reads only; writes nothing and never touches an installation.
# This does not prove anything in game (rendering, clicks, cursor, input block).
$ErrorActionPreference='Stop'
if(!$GameBinary) { $GameBinary=Join-Path $GameDir 'GameAssembly.dll' }
if(!$Dll) { $Dll=Join-Path $PSScriptRoot 'bin/Release/net6.0/SprocketUiKit.dll' }
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/Mono.Cecil.dll')
Add-Type -Path (Join-Path $GameDir 'BepInEx/core/SemanticVersioning.dll')
$Guid='sprocket.uikit'; $Version='0.1.12'; $KeybindsGuid='sprocket.keybinds'

# 1. Game binary (Sprocket 0.2.56.2)
$hash=(Get-FileHash -LiteralPath $GameBinary).Hash
if($hash -ne '3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B') { throw "Game binary changed ($hash); recheck hooks before installation." }
'PASS game binary is Sprocket 0.2.56.2.'

# 2. Hook targets: assembly, type, method, exact parameter types; return type for the input block.
$checks=@(
 @('Sprocket.Inputs','Sprocket.Inputsystem.Inputs','Update','',''),
 @('Sprocket.VehicleController','Sprocket.VehicleControl.VehicleInputSystemInputs','Fire','Sprocket.Controls.InputState','System.Boolean'),
 @('Sprocket.VehicleController','Sprocket.VehicleControl.VehicleInputSystemInputs','Scope','Sprocket.Controls.InputState','System.Boolean'),
 @('Sprocket.VehicleController','Sprocket.VehicleControl.VehicleInputSystemInputs','LockAim','Sprocket.Controls.InputState','System.Boolean'),
 @('Sprocket.VehicleController','Sprocket.VehicleControl.VehicleInputSystemInputs','CommandTeam','Sprocket.Controls.InputState','System.Boolean'),
 @('Sprocket.VehicleController','Sprocket.VehicleControl.VehicleInputSystemInputs','CommandTarget','Sprocket.Controls.InputState','System.Boolean'),
 @('Sprocket.VehicleController','Sprocket.VehicleControl.VehicleInputSystemInputs','CancelOrder','Sprocket.Controls.InputState','System.Boolean'),
 @('Sprocket.VehicleController','Sprocket.VehicleControl.VehicleInputSystemInputs','ShowDetailedInfo','Sprocket.Controls.InputState','System.Boolean'),
 @('Sprocket.CameraControl','Sprocket.OrbitalMovementController','ProcessInputs','System.Single,Sprocket.CameraInputProccessingFlags','Sprocket.CameraInputProccessingFlags'),
 @('Sprocket.CameraControl','Sprocket.ScopeCameraController','ProcessInputs','System.Single,Sprocket.CameraInputProccessingFlags','Sprocket.CameraInputProccessingFlags'),
 @('Sprocket.Pausing','Sprocket.GameTime','RequestPause','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1<Sprocket.PauseMenuOption>','System.Void')
)
# Members read or written (not hooked): assembly, type, method, parameter types.
$members=@(
 @('Sprocket.Inputs','Sprocket.Inputsystem.Inputs','set_pointerOverGUI','System.Boolean'),
 @('Sprocket.Inputs','Sprocket.Inputsystem.Inputs','get_pointer0',''),
 @('Sprocket.Inputs','Sprocket.Inputsystem.Inputs','set_pointer0','Sprocket.Inputsystem.ButtonInfo'),
 @('Sprocket.Inputs','Sprocket.Inputsystem.Inputs','set_pointer1','Sprocket.Inputsystem.ButtonInfo'),
 @('Sprocket.Inputs','Sprocket.Inputsystem.Inputs','set_pointer2','Sprocket.Inputsystem.ButtonInfo'),
 @('Sprocket.Inputs','Sprocket.Inputsystem.ButtonInfo','.ctor','System.Boolean,System.Boolean,System.Boolean,System.Boolean'),
 @('UnityEngine.CoreModule','UnityEngine.Sprite','Create','UnityEngine.Texture2D,UnityEngine.Rect,UnityEngine.Vector2,System.Single,System.UInt32,UnityEngine.SpriteMeshType,UnityEngine.Vector4'),
 @('UnityEngine.UI','UnityEngine.UI.Image','set_pixelsPerUnitMultiplier','System.Single'),
 @('UnityEngine.UI','UnityEngine.UI.Image','set_type','UnityEngine.UI.Image/Type'),
 @('UnityEngine.UI','UnityEngine.UI.Image','set_fillMethod','UnityEngine.UI.Image/FillMethod'),
 @('UnityEngine.UI','UnityEngine.UI.Image','set_fillAmount','System.Single'),
 @('UnityEngine.UI','UnityEngine.UI.Image','set_fillOrigin','System.Int32'),
 @('UnityEngine.UI','UnityEngine.UI.Image','set_fillClockwise','System.Boolean'),
 @('UnityEngine.CoreModule','UnityEngine.Mesh','set_vertices','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<UnityEngine.Vector3>'),
 @('UnityEngine.CoreModule','UnityEngine.Mesh','set_colors32','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<UnityEngine.Color32>'),
 @('UnityEngine.CoreModule','UnityEngine.Mesh','set_uv','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<UnityEngine.Vector2>'),
 @('UnityEngine.CoreModule','UnityEngine.Mesh','set_triangles','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Int32>'),
 @('UnityEngine.CoreModule','UnityEngine.Mesh','set_indexFormat','UnityEngine.Rendering.IndexFormat'),
 @('UnityEngine.CoreModule','UnityEngine.Mesh','get_indexFormat',''),
 @('UnityEngine.CoreModule','UnityEngine.Mesh','Clear',''),
 @('UnityEngine.UIModule','UnityEngine.CanvasRenderer','SetMesh','UnityEngine.Mesh'),
 @('UnityEngine.UIModule','UnityEngine.CanvasRenderer','SetMaterial','UnityEngine.Material,UnityEngine.Texture'),
 @('UnityEngine.UIModule','UnityEngine.CanvasRenderer','set_materialCount','System.Int32'),
 @('UnityEngine.UIModule','UnityEngine.CanvasRenderer','EnableRectClipping','UnityEngine.Rect'),
 @('UnityEngine.UI','UnityEngine.UI.Graphic','get_defaultGraphicMaterial',''),
 @('UnityEngine.UI','UnityEngine.UI.RectMask2D','get_canvasRect',''),
 @('UnityEngine.UI','UnityEngine.UI.RectMask2D','get_padding',''),
 @('UnityEngine.UIModule','UnityEngine.CanvasRenderer','set_cull','System.Boolean'),
 @('UnityEngine.UIModule','UnityEngine.Canvas','get_rootCanvas',''),
 @('UnityEngine.CoreModule','UnityEngine.Transform','TransformPoint','UnityEngine.Vector3'),
 @('UnityEngine.CoreModule','UnityEngine.Transform','InverseTransformPoint','UnityEngine.Vector3'),
 @('UnityEngine.CoreModule','UnityEngine.Application','get_isFocused',''),
 @('UnityEngine.CoreModule','UnityEngine.SceneManagement.Scene','get_handle',''),
 @('UnityEngine.CoreModule','UnityEngine.SceneManagement.SceneManager','GetActiveScene',''),
 @('UnityEngine.UIModule','UnityEngine.Canvas','set_scaleFactor','System.Single'),
 @('Unity.TextMeshPro','TMPro.TMP_Settings','get_defaultFontAsset',''),
 @('Unity.TextMeshPro','TMPro.TMP_Text','GetPreferredValues','System.String,System.Single,System.Single'),
 @('Unity.InputSystem','UnityEngine.InputSystem.Mouse','get_scroll',''),
 @('Unity.InputSystem','UnityEngine.InputSystem.Mouse','get_rightButton',''),
 @('Unity.InputSystem','UnityEngine.InputSystem.Mouse','get_middleButton',''),
 @('Sprocket.Pausing','Sprocket.GameTime','get_PauseState',''),
 @('UnityEngine.CoreModule','UnityEngine.Texture2D','LoadRawTextureData','System.IntPtr,System.Int32'),
 @('UnityEngine.CoreModule','UnityEngine.Camera','WorldToScreenPoint','UnityEngine.Vector3'),
 @('UnityEngine.UI','UnityEngine.UI.RawImage','set_uvRect','UnityEngine.Rect'),
 @('UnityEngine.UIModule','UnityEngine.CanvasGroup','set_alpha','System.Single'),
 @('Unity.TextMeshPro','TMPro.TMP_Text','set_outlineWidth','System.Single')
)
$assemblies=@{}
function Asm($name){ if(!$assemblies.ContainsKey($name)){ $assemblies[$name]=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDir "BepInEx/interop/$name.dll")) }; $assemblies[$name] }
function Sig($m){ ($m.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join ',' }
try {
 foreach($c in $checks) {
  $t=(Asm $c[0]).MainModule.Types | Where-Object FullName -EQ $c[1]
  if(!$t) { throw "Missing type $($c[1])" }
  $m=@($t.Methods | Where-Object Name -EQ $c[2])
  if($m.Count -ne 1) { throw "Missing or ambiguous $($c[1])::$($c[2])" }
  if((Sig $m[0]) -cne $c[3]) { throw "Changed signature $($c[1])::$($c[2]): $(Sig $m[0])" }
  if($c[4] -and $m[0].ReturnType.FullName -cne $c[4]) { throw "Changed return type $($c[1])::$($c[2]): $($m[0].ReturnType.FullName)" }
  "PASS hook $($c[1])::$($c[2])($($c[3]))"
 }
 foreach($c in $members) {
  $t=(Asm $c[0]).MainModule.Types | Where-Object FullName -EQ $c[1]
  if(!$t) { throw "Missing type $($c[1])" }
  $m=@($t.Methods | Where-Object { $_.Name -eq $c[2] -and (Sig $_) -ceq $c[3] })
  if($m.Count -lt 1) { throw "Missing member $($c[1])::$($c[2])($($c[3]))" }
  if($c[1] -in @('UnityEngine.Mesh','UnityEngine.CanvasRenderer','UnityEngine.UI.Graphic','UnityEngine.UI.RectMask2D')) { "PASS graph interop $($c[1])::$($c[2])($($c[3]))" }
 }
 "PASS $($members.Count) read/write members (Inputs, ButtonInfo, Sprite.Create 9-slice, Image, Canvas, TMP, Mouse; 0.1.2: GameTime.PauseState, LoadRawTextureData, RawImage, CanvasGroup, Camera, TMP outline; 0.1.8: Image Filled/Radial360 fillAmount/fillOrigin/fillClockwise; 0.1.9: Mesh channels/indexFormat/Clear, CanvasRenderer mesh/material/clip, Graphic default material, RectMask2D canvasRect)."
} finally { foreach($a in $assemblies.Values){ $a.Dispose() } }

# 3. Keybinds API present and compatible
$apiPath=Join-Path $GameDir 'BepInEx/plugins/SprocketKeybinds/SprocketKeybinds.dll'
$api=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($apiPath)
try {
 $apiVersion="$($api.Name.Version.Major).$($api.Name.Version.Minor).$($api.Name.Version.Build)"
 foreach($pair in @(@('SprocketKeybinds.Keybinds','RegisterButton'),@('SprocketKeybinds.Keybinds','Unregister'),@('SprocketKeybinds.Keybinds','get_IsConfiguring'),@('SprocketKeybinds.ModKeybind','get_WasPressedThisFrame'),@('SprocketKeybinds.ModKeybind','GetBindingDisplayString'))) {
  $type=$api.MainModule.Types | Where-Object FullName -eq $pair[0]
  if(!($type.Methods | Where-Object Name -eq $pair[1])){ throw "Keybinds API member missing: $($pair[0])::$($pair[1])" }
 }
} finally { $api.Dispose() }

# 4. Built DLL: identity, dependency, callbacks, demo keybind
$dll=$Dll
if(!(Test-Path $dll)) { throw "$dll missing: build first (dotnet build -c Release)." }
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
 if($a.Name.Name -ne 'SprocketUiKit' -or $a.Name.Version -ne [version]"$Version.0") { throw "Assembly identity $($a.Name.Name) $($a.Name.Version)" }
 $plugin=$a.MainModule.Types | Where-Object FullName -eq 'SprocketUiKit.Plugin'
 $info=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' })
 if($info.Count -ne 1 -or $info[0].ConstructorArguments[0].Value -ne $Guid -or $info[0].ConstructorArguments[2].Value -ne $Version) { throw 'BepInPlugin GUID/version mismatch.' }
 $dep=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' })
 if($dep.Count -ne 1 -or $dep[0].ConstructorArguments[0].Value -ne $KeybindsGuid -or $dep[0].ConstructorArguments[1].Value -ne '>=0.1.3 <0.2.0') { throw 'Keybinds dependency must be >=0.1.3 <0.2.0.' }
 if(![SemanticVersioning.Range]::Parse('>=0.1.3 <0.2.0', $false).IsSatisfied($apiVersion)) { throw "Installed Keybinds $apiVersion outside the dependency range." }
 "PASS identity $Guid $Version (assembly $($a.Name.Version)); Keybinds dependency >=0.1.3 <0.2.0 accepts installed $apiVersion."
 if(![SemanticVersioning.Range]::Parse('>=0.1.9 <0.2.0', $false).IsSatisfied($Version) -or [version]$Version -le [version]'0.1.9') { throw '0.1.12 must numerically exceed 0.1.9 and satisfy >=0.1.9 <0.2.0.' }
 'PASS consumer range >=0.1.9 <0.2.0 accepts 0.1.12; numeric version exceeds 0.1.9.'
 $api=$a.MainModule.Types | Where-Object FullName -eq 'SprocketUiKit.UiKit'
 foreach($f in @(@('PluginGuid',$Guid),@('Version',$Version))) { $field=$api.Fields | Where-Object Name -eq $f[0]; if(!$field -or $field.Constant -ne $f[1]) { throw "UiKit.$($f[0]) must be '$($f[1])'" } }
 foreach($m in @('CreatePanel','BindToggle','DestroyPanel','CreateHud','DestroyHud','CreateMarker','DestroyMarker')) { if(!($api.Methods | Where-Object { $_.Name -eq $m -and $_.IsPublic })) { throw "Public API UiKit.$m missing" } }
 $strings=@(); $calls=@{}
 foreach($type in $a.MainModule.GetTypes()) { foreach($method in $type.Methods) {
  $graphType=$type.FullName -match '^SprocketUiKit\.(NodeGraph|GraphNode|GraphPort|GraphWire|GraphNodeView|Demo019)(/|$)'
  if($graphType -and $type.BaseType.FullName -like 'UnityEngine.*') { throw "Native Unity subclass in graph: $($type.FullName)" }
  $harmony=@($method.CustomAttributes | Where-Object { $_.AttributeType.FullName -like 'HarmonyLib.Harmony*' }).Count
  if($harmony) { foreach($p in $method.Parameters) { if($p.ParameterType.IsValueType -or $p.ParameterType.IsByReference) { throw "Struct/by-ref parameter in Harmony callback $($method.FullName)" } } }
  if($graphType -and $harmony) { throw "Harmony callback in graph: $($method.FullName)" }
  if($type.FullName -eq 'SprocketUiKit.InputBlock' -and $method.Name -like 'Block*') {
   if($method.Parameters.Count -ne 1 -or $method.Parameters[0].Name -ne '__result' -or !$method.Parameters[0].ParameterType.IsByReference) { throw "Input block prefix must take only ref __result: $($method.FullName)" }
  }
  if(!$method.HasBody) { continue }
  foreach($i in $method.Body.Instructions) {
   if($i.OpCode.Code -eq 'Ldstr') { $strings+=$i.Operand }
   if($i.Operand -is [Mono.Cecil.MethodReference]) {
    $n=$i.Operand.FullName
    if($graphType -and $n -match '(HarmonyLib\.|ClassInjector|SprocketUiKit\.(CanvasView|Raster)::|UnityEngine.EventSystems\.|System.Reflection\.)') { throw "Forbidden graph route: $($method.FullName) -> $n" }
    if($n -match 'DelegateSupport::ConvertDelegate') { throw "Delegate conversion found in $($method.FullName) (only UnityAction is proven; the kit polls instead)" }
    $key="$($type.FullName)::$($method.Name)"
    if(!$calls.ContainsKey($key)){ $calls[$key]=@() }; $calls[$key]+=$n
    # Direct key/button polling only in the kit's UI input methods (mouse clicks, typed text), never for gameplay.
    if($n -cmatch 'InputSystem\.Controls\.ButtonControl::get_(isPressed|wasPressedThisFrame|wasReleasedThisFrame)' -and $key -notmatch '^SprocketUiKit\.Kit::(ProcessPointer|ProcessKeyboard|TypeKeys|EscapeBelongsToKit)$') { throw "Direct button polling outside the kit UI input methods: $key" }
   }
  }
 }}
 foreach($s in @('demo','UI Kit / Demo','Diagnostics','DetailedLogging')) { if($strings -notcontains $s) { throw "Missing string: $s" } }
 if($strings -contains '<Keyboard>/f6') { throw 'Demo keybind must be unbound by default (0.1.2, F6 clashes with QoL flashlight).' }
 $register=$calls['SprocketUiKit.Demo::Register']
 if(!($register | Where-Object { $_ -match 'SprocketKeybinds.Keybinds::RegisterButton' })) { throw 'Demo keybind must be registered through Keybinds.RegisterButton.' }
 if(!($register | Where-Object { $_ -match 'SprocketUiKit.UiKit::BindToggle' })) { throw 'Demo panel must be toggled through UiKit.BindToggle.' }
 if(!($calls['SprocketUiKit.Kit::PollToggles'] | Where-Object { $_ -match 'SprocketKeybinds.ModKeybind::get_WasPressedThisFrame' })) { throw 'Bound toggles must poll ModKeybind.WasPressedThisFrame.' }
 'PASS Harmony callbacks (no struct/by-ref except input-block ref __result), no ConvertDelegate, direct polling only in UI input methods.'
 'PASS graph has no native subclass, Harmony hook, ClassInjector, EventSystem, consumer reflection or CanvasView/Raster route.'
 $graph=$a.MainModule.Types | Where-Object FullName -eq 'SprocketUiKit.NodeGraph'
 $upload=$graph.Methods | Where-Object Name -eq 'UploadFullMesh'
 $uploadCalls=@($upload.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -in @('UnityEngine.Mesh','UnityEngine.CanvasRenderer') } | ForEach-Object { $_.Operand.Name })
 $expected='Clear,set_vertices,set_uv,set_colors32,set_triangles,SetMesh,get_indexFormat'
 if(($uploadCalls -join ',') -cne $expected) { throw "Graph full upload order changed: $($uploadCalls -join ',')" }
 $build=$graph.Methods | Where-Object Name -eq 'Build'
 $wireRenderers=@($build.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.GenericInstanceMethod] -and $_.Operand.Name -eq 'AddComponent' -and $_.Operand.GenericArguments[0].FullName -eq 'UnityEngine.CanvasRenderer' })
 if($wireRenderers.Count -ne 1) { throw 'Graph must create exactly one wire CanvasRenderer in Build.' }
 $indexSetter=@($build.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -match 'Mesh::set_indexFormat' })
 if($indexSetter.Count -ne 1 -or $indexSetter[0].Previous.OpCode.Code -ne 'Ldc_I4_0') { throw 'Graph Mesh indexFormat must be UInt16 (0).' }
 if($strings -notcontains 'Mast pattern') { throw 'Graph demo Mast pattern button missing.' }
 'PASS graph IL: UInt16, one wire CanvasRenderer creation, full upload Clear -> vertices -> uv -> colors32 -> triangles -> SetMesh; Mast pattern present.'
 # 0.1.11: one throttled asset scan, shared ownership, lifecycle restore, additive graph API.
 $guard=$a.MainModule.Types | Where-Object FullName -eq 'SprocketUiKit.KeyboardGuard'
 $tick=$guard.Methods | Where-Object Name -eq 'Tick'
 $scan=@($tick.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'FindObjectsOfTypeAll' })
 $due=@($tick.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'SprocketUiKit.RescanThrottle' -and $_.Operand.Name -eq 'Due' })
 if($scan.Count -ne 1 -or $due.Count -lt 1 -or $due[-1].Offset -ge $scan[0].Offset) { throw 'KeyboardGuard must gate its single asset scan with RescanThrottle.Due.' }
 if($calls['SprocketUiKit.KeyboardGuard::Tick'] -match 'SetSelectedGameObject') { throw 'Panel focus must not select a typing proxy.' }
 if(!($calls['SprocketUiKit.TypingGuard::Tick'] -match 'KeyboardGuard::Tick')) { throw 'TypingGuard must share the throttled guard.' }
 if(!($calls['SprocketUiKit.UiKitHost::OnApplicationFocus'] -match 'Kit::ReleaseKeyboard')) { throw 'Alt-tab release path missing.' }
 if(!($calls['SprocketUiKit.Kit::Shutdown'] -match 'Kit::ReleaseKeyboard')) { throw 'Unload release path missing.' }
 $release=@($tick.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ReleaseKeyboard' })
 $report=@($tick.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Report' })
 if($release.Count -ne 1 -or $report.Count -ne 1 -or $release[0].Offset -ge $report[0].Offset) { throw 'Suspension exception must restore before reporting.' }
 foreach($name in @('OnPortDrag','PortHitRadius','get_ViewSize','get_VisibleRect')) {
  if(!($graph.Methods | Where-Object { $_.IsPublic -and $_.Name -eq $name })) { throw "Missing NodeGraph API: $name" }
 }
 if($strings -notcontains 'BlockKeysWhilePanelFocused') { throw 'Panel keyboard block config missing.' }
 # Verify actual runtime wiring as well as the pure state-machine tests.
 $panelType=$a.MainModule.Types | Where-Object FullName -eq 'SprocketUiKit.Panel'
 foreach($pair in @(@($panelType,'HasFocus','System.Boolean',$false),@($api,'FocusedPanel','SprocketUiKit.Panel',$true))) {
  $prop=$pair[0].Properties | Where-Object Name -eq $pair[1]
  if(!$prop -or $prop.SetMethod -or !$prop.GetMethod.IsPublic -or $prop.GetMethod.IsStatic -ne $pair[3] -or $prop.PropertyType.FullName -ne $pair[2]) { throw "Invalid read-only focus property: $($pair[1])" }
  $body=@($prop.GetMethod.Body.Instructions | Where-Object { $_.OpCode.Code -ne 'Nop' })
  $expected=if($pair[1] -eq 'HasFocus'){'Call,Ldarg_0,Ceq,Ret'}else{'Call,Ret'}
  if(($body.OpCode.Code -join ',') -cne $expected -or $body[0].Operand.FullName -ne 'SprocketUiKit.Panel SprocketUiKit.Kit::get_KeyboardFocus()') { throw "Focus getter must project Kit.KeyboardFocus: $($pair[1])" }
 }
 $kitType=$a.MainModule.Types | Where-Object FullName -eq 'SprocketUiKit.Kit'
 $process=$kitType.Methods | Where-Object Name -eq 'ProcessKeyboard'
 $policyCalls=@($process.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'SprocketUiKit.InputFocusPolicy' -and $_.Operand.Name -eq 'ShortcutTarget' })
 if($policyCalls.Count -ne 1 -or !($calls['SprocketUiKit.Kit::ProcessKeyboard'] -match 'Kit::get_KeyboardFocus')) { throw 'Keyboard dispatch must use the pure focus-only policy.' }
 'PASS focus API getter IL and ProcessKeyboard pure-policy wiring.'
 'PASS keyboard guard IL: shared throttled scan, proxy-free panel focus, alt-tab/unload release, restore before exception report; new graph APIs present.'
 'PASS demo keybind "UI Kit / Demo" (factory default unbound, no F6) registered via Keybinds.RegisterButton, toggled via UiKit.BindToggle -> ModKeybind.WasPressedThisFrame.'
} finally { $a.Dispose() }

# 4b. Public API compatibility (additive 0.1.x): every public/protected type, member and enum value of
# every baseline\SprocketUiKit-<version>.dll still exists unchanged in this build, so consumers asking
# ">=0.1.x <0.2.0" keep working without a rebuild. The baseline of the current version is the released DLL itself:
# it must match exactly. When a new version is released, add its DLL to baseline\ (do not remove older ones).
function Visible($t){ while($t){ if(!($t.IsPublic -or $t.IsNestedPublic)){ return $false }; $t=$t.DeclaringType }; $true }
function Kind($t){ if($t.IsEnum){'enum'} elseif($t.IsInterface){'interface'} elseif($t.IsValueType){'struct'} elseif($t.BaseType -and $t.BaseType.FullName -eq 'System.MulticastDelegate'){'delegate'} else {'class'} }
function Surface($path){
 $asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($path); $set=New-Object 'System.Collections.Generic.HashSet[string]'
 try {
  foreach($t in $asm.MainModule.GetTypes()) {
   if(!(Visible $t)) { continue }
   $n=$t.FullName
   [void]$set.Add("type $(Kind $t) $n sealed=$($t.IsSealed) abstract=$($t.IsAbstract) base=$($t.BaseType.FullName) generic=$($t.GenericParameters.Count)")
   foreach($i in $t.Interfaces){ [void]$set.Add("iface $n : $($i.InterfaceType.FullName)") }
   foreach($f in $t.Fields){
    if(!($f.IsPublic -or $f.IsFamily -or $f.IsFamilyOrAssembly)) { continue }
    if($t.IsEnum){ if($f.IsLiteral){ [void]$set.Add("enum $n.$($f.Name)=$($f.Constant)") }; continue }
    $c= if($f.IsLiteral -and !($n -eq 'SprocketUiKit.UiKit' -and $f.Name -eq 'Version')) { "=$($f.Constant)" } else { '' }
    [void]$set.Add("field $n.$($f.Name) : $($f.FieldType.FullName) static=$($f.IsStatic) readonly=$($f.IsInitOnly) const=$($f.IsLiteral)$c")
   }
   foreach($m in $t.Methods){
    if(!($m.IsPublic -or $m.IsFamily -or $m.IsFamilyOrAssembly)) { continue }
    $ps=($m.Parameters | ForEach-Object { "$($_.ParameterType.FullName)$(if($_.IsOptional){'=' + $_.Constant})" }) -join ','
    $vis= if($m.IsPublic){'public'} else {'protected'}
    [void]$set.Add("method $vis $n::$($m.Name)``$($m.GenericParameters.Count)($ps) : $($m.ReturnType.FullName) static=$($m.IsStatic) virtual=$($m.IsVirtual -and !$m.IsFinal) abstract=$($m.IsAbstract)")
   }
  }
 } finally { $asm.Dispose() }
 ,$set
}
$new=Surface $dll
$baselines=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'baseline') -Filter 'SprocketUiKit-*.dll' -ErrorAction SilentlyContinue | Sort-Object { [version]($_.BaseName -replace '^SprocketUiKit-','') })
if(!$baselines.Count) { throw "No baseline\SprocketUiKit-<version>.dll found." }
if(!($baselines | Where-Object BaseName -eq "SprocketUiKit-$Version")) { throw "baseline\SprocketUiKit-$Version.dll missing (copy of the released $Version)." }
foreach($b in $baselines) {
 $baseVersion=$b.BaseName -replace '^SprocketUiKit-',''
 $old=Surface $b.FullName
 $missing=@($old | Where-Object { !$new.Contains($_) })
 if($missing.Count) { $missing | ForEach-Object { "MISSING/CHANGED: $_" }; throw "Public API of $baseVersion changed or removed ($($missing.Count) entries): not additive." }
 if($baseVersion -eq $Version) {
  $added=@($new | Where-Object { !$old.Contains($_) } | Sort-Object)
  if($added.Count) { $added | ForEach-Object { "ADDED: $_" }; throw "Public API differs from the released $Version baseline ($($added.Count) added entries): bump the version and add a new baseline." }
  "PASS public API identical to the released $Version baseline ($($old.Count) entries)."
 } else {
  "PASS public API compatibility: all $($old.Count) public entries of $baseVersion unchanged in $Version; $($new.Count - $old.Count) additive entries."
 }
}

# 5. Source and packaging rules
# Hooks.cs is pinned by hash: any change to the Harmony hooks must be a deliberate, reviewed edit (then update the hash here).
$hooks=Join-Path $PSScriptRoot 'Runtime/Hooks.cs'
$hooksHash='D0400905EE3C56A0A7C68C31740D7755E6890F3123E2A467A3E029B3CAA03A8A'
if((Get-FileHash -LiteralPath $hooks).Hash -ne $hooksHash) { throw 'Hooks.cs changed: review the Harmony hooks, then update the pinned hash in Verify.ps1.' }
'PASS Hooks.cs matches the pinned hash (no changed hooks).'
$kitSource=Get-Content -Raw (Join-Path $PSScriptRoot 'Runtime/Kit.cs')
$start=$kitSource.IndexOf('    private static void ProcessKeyboard()')
$end=$kitSource.IndexOf('    // Esc that closed a popup')
$cut=$kitSource.IndexOf('        var shortcutOwner = InputFocusPolicy.ShortcutTarget')
if($start -lt 0 -or $end -lt $start -or $cut -lt $start) { throw 'Kit.cs ProcessKeyboard keyboard recipient block not found.' }
$dispatch=$kitSource.Substring($cut,$end-$cut)
if($dispatch -match '\btop\b' -or $dispatch -notmatch 'shortcutOwner\.ModalOpen' -or $dispatch -notmatch 'shortcutOwner\.ModalEnter\(\)' -or $dispatch -notmatch 'shortcutOwner\.DefaultActionHandler' -or $dispatch -notmatch 'shortcutOwner\.Shortcuts\.ToList\(\)') { throw 'Shortcut/Enter must share the focus owner, including its modal/default action, with a mutation-safe snapshot.' }
'PASS keyboard dispatch: Enter and shortcuts use the focused panel (modal/default action), mutation-safe snapshot.'

$plugin=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Runtime/Plugin.cs')
if($plugin -notmatch 'Config\.Bind\("Diagnostics", "DetailedLogging", true,') { throw 'Diagnostics.DetailedLogging must exist (default true in this development build).' }
[xml]$project=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'SprocketUiKit.csproj')
$reference=@($project.Project.ItemGroup.Reference | Where-Object Include -eq 'SprocketKeybinds')
if($reference.Count -ne 1 -or $reference[0].Private -ne 'false') { throw 'Keybinds API must be referenced Private=false.' }
if(Test-Path (Join-Path (Split-Path -Parent $dll) 'SprocketKeybinds.dll')) { throw 'Do not ship a Keybinds API copy.' }
'PASS DetailedLogging default true, Keybinds reference Private=false, no Keybinds copy next to the UI Kit DLL.'
'ALL PASS. Not proven here: drawing, font, clicks, typing, cursor, click-through and input block in game (see TESTPLAN.md).'
