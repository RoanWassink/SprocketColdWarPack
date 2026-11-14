param([string]$GameDir,[string]$Report,[string]$NativeMethodsJson='')
$ErrorActionPreference='Stop'
if(-not $NativeMethodsJson -or -not (Test-Path -LiteralPath $NativeMethodsJson)){throw 'Pass -NativeMethodsJson: the all-native-methods.json of the ICF audit (read-only).'}
$expected='3E58082804D97CF32FFD400E7427E767EF9576D23DB71B2BE96FBB7F825FC17B'
$hash=(Get-FileHash -LiteralPath (Join-Path $GameDir 'GameAssembly.dll') -Algorithm SHA256).Hash
if($hash -ne $expected){throw 'GameAssembly changed; H3 has not been approved for this binary.'}
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')
$dump=Join-Path $GameDir 'MLLoader\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out\Sprocket.VehiclePartImporting.dll'
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dump)
try {
  $type=@($a.MainModule.Types | Where-Object FullName -eq 'Sprocket.PartImporting.PartDefinitionCardFactory')[0]
  $method=@($type.Methods | Where-Object {$_.Name -eq 'CreateCard' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'Sprocket.PartImporting.PartDefinition'})
  if($method.Count -ne 1){throw 'H3 native signature is missing or ambiguous.'}
  $attribute=$method[0].CustomAttributes | Where-Object {$_.AttributeType.Name -eq 'AddressAttribute'}
  $rva=($attribute.Fields | Where-Object Name -eq 'RVA').Argument.Value
  $old=Get-Content -LiteralPath $NativeMethodsJson -Raw | ConvertFrom-Json
  $peers=@($old | Where-Object {[Convert]::ToInt64($_.RVA,16) -eq [Convert]::ToInt64($rva,16)})
  if($rva -ne '0x200FA10' -or $peers.Count -ne 1 -or $peers[0].Length -ne '0x171' -or $peers[0].Name -ne $method[0].FullName){throw 'H3 ICF/RVA/length check failed.'}
  @("GameAssembly SHA256=$hash","H3=$($method[0].FullName)","RVA=$rva bytes=$($peers[0].Length) ICF peers=$($peers.Count) PASS",'Other Reference Images hooks: none.',
    'Session identity: IVehicleGateway.gameObject.GetInstanceID(), retained native Unity wrapper + monotonic managed key.',
    'Vehicle.EnableBehaviour/DisableBehaviour change behavior, not the gateway identity; actual designer/test-drive lifecycle remains a live gate.',
    'Opening a design that creates a new native Vehicle identity safely yields NO IMAGE; no cross-vehicle heuristic or blueprint read is used.') | Set-Content -LiteralPath $Report -Encoding UTF8
  Write-Output "H3 PASS: $rva, 0x171 bytes, ICF=1, current hash $hash"
} finally {$a.Dispose()}
