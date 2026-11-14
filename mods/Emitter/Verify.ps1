[CmdletBinding()]
param(
    # Real game folder: READ ONLY (stock part template, GameAssembly hash, Cpp2IL dump for the interop scan).
    [string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Sprocket',
    # Folder with BepInEx\core, interop and unity-libs to compile against (default: the game folder).
    [string]$BuildGameDir='',
    # Everything this script produces goes here (build output, logs). Default: .\out next to this script.
    [string]$WorkDir='',
    # Optional: Make-PartIcon.ps1 of the sprocket-bepinex skill. Without it a built-in minimal icon check runs.
    [string]$PartIconScript='',
    [string]$Version='0.1.1')
# The single gate for Emitter. Reads the game folder, writes only under $WorkDir. Installs nothing.
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(-not $BuildGameDir){$BuildGameDir=$GamePath}
if(-not $WorkDir){$WorkDir=Join-Path $root 'out'}
$WorkDir=[IO.Path]::GetFullPath($WorkDir)
if($WorkDir.StartsWith([IO.Path]::GetFullPath($GamePath).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'WorkDir must not be inside the game folder.'}
$out=Join-Path $WorkDir $Version
$logs=Join-Path $WorkDir "logs\gate-$Version"
if(Test-Path -LiteralPath $out){Remove-Item -LiteralPath $out -Recurse -Force}
New-Item -ItemType Directory -Force -Path $out,$logs | Out-Null
$summary=[Collections.Generic.List[string]]::new()
function Step([string]$Name,[scriptblock]$Body){
    $log=Join-Path $logs "$Name.txt"
    $global:LASTEXITCODE=0
    $text=& $Body 2>&1 | ForEach-Object {"$_"}
    $text | Set-Content -LiteralPath $log -Encoding UTF8
    if($LASTEXITCODE){throw "Gate step $Name failed (exit $LASTEXITCODE); see $log"}
    $last=@($text | Where-Object {$_ -match 'SUMMARY|PASS|Warning\(s\)|0 FAIL'} | Select-Object -Last 1)
    $script:summary.Add(("{0,-12} {1}" -f $Name,($last -join '')))
    Write-Output "[gate] $Name ok -> $log"
}
Push-Location $root
try{
    Step 'sdk' {$v=dotnet --version;if($v -ne '8.0.425'){throw "SDK $v, expected pinned 8.0.425"};"PASS SDK $v (global.json pinned)"}
    Step 'build' {
        dotnet build SprocketEmitter.csproj -c Release --nologo --no-incremental -o (Join-Path $out 'build') "-p:GameDir=$BuildGameDir" -p:ContinuousIntegrationBuild=true
    }
    $buildLog=Get-Content -LiteralPath (Join-Path $logs 'build.txt') -Raw
    if($buildLog -notmatch '\s0 Warning\(s\)' -or $buildLog -notmatch '\s0 Error\(s\)'){throw 'Build is not warning-free.'}
    Copy-Item -LiteralPath (Join-Path $out 'build\SprocketEmitter.dll') -Destination (Join-Path $out 'SprocketEmitter.dll')
    $dll=Join-Path $out 'SprocketEmitter.dll'
    Step 'contract' {py -I tools\check-contract.py $GamePath $dll}
    Step 'mesh' {
        py -I tools\generate-mesh.py --check --evidence (Join-Path $logs 'mesh-evidence.json')
        if($LASTEXITCODE){return}
        py -I assets\verify-arrow.py (Join-Path $WorkDir 'scratch')
    }
    Step 'logic' {
        dotnet build checks\Logic\Logic.csproj -c Release --nologo -v q -o (Join-Path $out 'checks\logic') | Out-Null
        if($LASTEXITCODE){throw 'Logic build failed'}
        dotnet (Join-Path $out 'checks\logic\Logic.dll')
    }
    Step 'eligibility' {
        dotnet build checks\Eligibility\Eligibility.csproj -c Release --nologo -v q -o (Join-Path $out 'checks\eligibility') | Out-Null
        if($LASTEXITCODE){throw 'Eligibility build failed'}
        dotnet (Join-Path $out 'checks\eligibility\Eligibility.dll')
    }
    Step 'interop' {
        & (Join-Path $root 'checks\Scan-InteropSpans.ps1') -ModDll $dll -GamePath $GamePath -BuildGameDir $BuildGameDir -Report (Join-Path $logs 'interop-scan.txt')
        $r=Get-Content -LiteralPath (Join-Path $logs 'interop-scan.txt')
        $start=[Array]::FindIndex($r,[Predicate[string]]{param($l)$l -like '== Native status not decidable offline*'})
        if($start -ge 0){$r[$start..($r.Length-1)] | Where-Object {$_ -match '^(==|  )'} | Set-Content -LiteralPath (Join-Path $logs 'interop-undecidable.txt') -Encoding UTF8}
        $modFails=@($r | Where-Object {$_ -match '^FAIL ' -and $_ -notmatch 'expected FAIL -> OK'}).Count
        if($modFails){throw "$modFails interop FAIL lines"}
        "PASS interop scan: 0 FAIL in mod calls"
    }
    Step 'icon' {
        $icon=Join-Path $root 'assets\emitter-icon.png'
        if($PartIconScript){& $PartIconScript -Check $icon}
        else{
            Add-Type -AssemblyName System.Drawing
            $b=[Drawing.Bitmap]::FromFile($icon)
            try{if($b.Width -ne 256 -or $b.Height -ne 256 -or $b.PixelFormat -ne [Drawing.Imaging.PixelFormat]::Format32bppArgb){throw 'icon must be 256x256, 32-bit RGBA'};"PASS $icon (256x256 RGBA; full skill check not run)"}
            finally{$b.Dispose()}
        }
    }
}finally{Pop-Location}
$dllHash=(Get-FileHash -LiteralPath (Join-Path $out 'SprocketEmitter.dll') -Algorithm SHA256).Hash
"$dllHash  SprocketEmitter.dll" | Set-Content -LiteralPath (Join-Path $out 'SHA256.txt') -Encoding ASCII
$summary.Add("SHA256 DLL   $dllHash")
$summary | Set-Content -LiteralPath (Join-Path $logs 'SUMMARY.txt') -Encoding UTF8
$summary
Write-Output "GATE $Version GREEN. Nothing installed. Logs: $logs"
