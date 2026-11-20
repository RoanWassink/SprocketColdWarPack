[CmdletBinding()]
param([string]$GamePath='E:\SteamLibrary\steamapps\common\Sprocket',[string]$NativeMethodsJson=$env:NATIVE_METHODS_JSON)
# The single gate for Plate Destruction P0 0.8.3: build, managed tests, ICF check, package. Reads the game folder, writes only under ..\out-p0.
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=[IO.Path]::GetFullPath((Join-Path $root '..\out-p0'))
$pkg=Join-Path $out 'package'
if(Test-Path -LiteralPath $pkg){Get-ChildItem -LiteralPath $pkg -Force | Remove-Item -Recurse -Force}  # contents only: the directory itself may be a working directory
New-Item -ItemType Directory -Force -Path $out,$pkg | Out-Null
Push-Location $root
try{
    # Mechanical check: non-blittable structs must never cross the native boundary by ref/out (native crash in 0.0.2).
    $bad=Get-ChildItem -LiteralPath $root -Filter *.cs -Recurse | Where-Object{$_.FullName -notmatch '\\(obj|bin)\\'} | Select-String -Pattern '(out|ref)\s+(var\s+)?PenetrationFragment|PenetrationFragment[^;\r\n]*\b(out|ref)\b|\bGetFragment\s*\('
    if($bad){$bad|ForEach-Object{Write-Output "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"};throw 'Forbidden PenetrationFragment out/ref or GetFragment( use in source.'}
    # Mechanical check: no Harmony patch on any serialisation / save / load method (the Destructable flag lives in the native description only).
    $ser=Get-ChildItem -LiteralPath $root -Filter *.cs -Recurse | Where-Object{$_.FullName -notmatch '[\x5C/](obj|bin|checks)[\x5C/]'} | Select-String -Pattern 'HarmonyPatch\([^)]*\)?[^)]*,\s*(nameof\([\w.]*|")\w*(Serialize|ToBlueprint|LoadData|Save)|\.Patch\([^;]*(Serialize|ToBlueprint|LoadData|Save)|AccessTools\.\w+\([^;]*"[^"]*(Serialize|ToBlueprint|LoadData|Save)'
    if($ser){$ser|ForEach-Object{Write-Output "$($_.Path):$($_.LineNumber): $($_.Line.Trim())"};throw 'Forbidden Harmony patch on a Serialize/ToBlueprint/LoadData/Save method.'}
    $v=dotnet --version;if($v -ne '8.0.425'){throw "SDK $v, expected pinned 8.0.425"}
    dotnet build SprocketPlateDestructionP0.csproj -c Release --nologo --no-incremental -o (Join-Path $out 'build') "-p:GameDir=$GamePath" -p:ContinuousIntegrationBuild=true | Tee-Object -FilePath (Join-Path $out 'build.log')
    $log=Get-Content -LiteralPath (Join-Path $out 'build.log') -Raw
    if($log -notmatch '\s0 Warning\(s\)' -or $log -notmatch '\s0 Error\(s\)'){throw 'Build is not warning-free.'}
    dotnet run --project checks\Logic\Logic.csproj -c Release --nologo | Tee-Object -FilePath (Join-Path $out 'tests.log')
    if($LASTEXITCODE){throw 'Managed tests failed.'}
    if($NativeMethodsJson){
        py -I checks\icf-check.py $root $NativeMethodsJson (Join-Path $GamePath 'GameAssembly.dll') (Join-Path $out 'icf-check.txt')
        if($LASTEXITCODE){throw 'ICF check failed.'}
    }
    $dll=Join-Path $out 'build\SprocketPlateDestructionP0.dll'
    $rel='BepInEx/plugins/SprocketPlateDestructionP0/SprocketPlateDestructionP0.dll'
    $dest=Join-Path (Join-Path $pkg 'payload') $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Copy-Item -LiteralPath $dll -Destination $dest
    $hash=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    [pscustomobject]@{Mod='nero.sprocket.platedestruction.p0';Version='0.8.3';Files=@([pscustomobject]@{Relative=$rel;SHA256=$hash})} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $pkg 'package-manifest.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $root 'Install-PlateDestructionP0.ps1') -Destination $pkg
    Copy-Item -LiteralPath $dll -Destination (Join-Path $out 'SprocketPlateDestructionP0.dll')
    "DLL SHA256 $hash" | Tee-Object -FilePath (Join-Path $out 'dll-sha256.txt')
    Write-Output 'GATE OK'
}finally{Pop-Location}
