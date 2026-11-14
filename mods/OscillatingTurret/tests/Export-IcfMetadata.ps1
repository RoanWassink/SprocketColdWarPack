# Read-only Cecil export of the game's types and native addresses (input of tests\icf_audit.py).
# No game assembly is loaded into the CLR or native runtime; the game folder is only read.
#   powershell -NoProfile -File Export-IcfMetadata.ps1 -Output <path of icf-metadata.json> [-GameDir <game folder>]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
    [Parameter(Mandatory = $true)][string]$Output
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')
$dummyDir = Join-Path $GameDir 'MLLoader\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out'
$native = [Collections.Generic.List[object]]::new()
$dummyTypes = [Collections.Generic.List[object]]::new()
$interopTypes = [Collections.Generic.List[object]]::new()
function Export-Types($types, $rows, [bool]$dummy) {
    foreach ($t in $types) {
        $keep = $t.FullName.StartsWith('Sprocket.') -or $t.FullName -in @(
            'System.Object', 'UnityEngine.Object', 'UnityEngine.Component',
            'UnityEngine.Behaviour', 'UnityEngine.MonoBehaviour')
        $methods = [Collections.Generic.List[object]]::new()
        foreach ($m in $t.Methods) {
            $rva = $null; $length = $null
            if ($dummy) {
                foreach ($attr in $m.CustomAttributes) {
                    if ($attr.AttributeType.Name -eq 'AddressAttribute') {
                        foreach ($f in $attr.Fields) {
                            if ($f.Name -eq 'RVA') { $rva = $f.Argument.Value }
                            if ($f.Name -eq 'Length') { $length = $f.Argument.Value }
                        }
                    }
                }
                if ($rva) { $native.Add([pscustomobject]@{ Name=$m.FullName; RVA=$rva; Length=$length }) }
            }
            if ($keep) {
                $methods.Add([pscustomobject]@{
                    name=$m.Name; signature=$m.FullName
                    parameters=@($m.Parameters | ForEach-Object { $_.ParameterType.FullName })
                    virtual=$m.IsVirtual; newSlot=$m.IsNewSlot; rva=$rva; length=$length
                })
            }
        }
        if ($keep) {
            $rows.Add([pscustomobject]@{ name=$t.FullName; base=$t.BaseType.FullName; methods=@($methods.ToArray()) })
        }
        if ($t.HasNestedTypes) { Export-Types $t.NestedTypes $rows $dummy }
    }
}
foreach ($file in Get-ChildItem -LiteralPath $dummyDir -Filter '*.dll' | Sort-Object Name) {
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($file.FullName)
    try { Export-Types $asm.MainModule.Types $dummyTypes $true } finally { $asm.Dispose() }
}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $GameDir 'BepInEx\interop') -Filter 'Sprocket*.dll' | Sort-Object Name) {
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($file.FullName)
    try { Export-Types $asm.MainModule.Types $interopTypes $false } finally { $asm.Dispose() }
}
[pscustomobject]@{
    gameSha256=(Get-FileHash -LiteralPath (Join-Path $GameDir 'GameAssembly.dll') -Algorithm SHA256).Hash
    dummyTypes=@($dummyTypes.ToArray()); interopTypes=@($interopTypes.ToArray()); native=@($native.ToArray())
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Output -Encoding utf8
Write-Output "Cecil metadata: $($dummyTypes.Count) native types; $($interopTypes.Count) interop types; $($native.Count) cross-assembly addresses -> $Output"
